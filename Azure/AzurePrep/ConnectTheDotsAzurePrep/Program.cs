﻿//  ---------------------------------------------------------------------------------
//  Copyright (c) Microsoft Open Technologies, Inc.  All rights reserved.
// 
//  The MIT License (MIT)
// 
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
// 
//  The above copyright notice and this permission notice shall be included in
//  all copies or substantial portions of the Software.
// 
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//  THE SOFTWARE.
//  ---------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Management.Storage;
using Microsoft.WindowsAzure.Management.Storage.Models;
using Microsoft.WindowsAzure.Management.ServiceBus;
using Microsoft.WindowsAzure.Management.ServiceBus.Models;
using System.Net;
using System.IO;
using Newtonsoft.Json;


namespace ConnectTheDotsAzurePrep
{
    class Program
    {
        // from cmd line
        string NamePrefix;
        string SBNamespace;
        string Location;
        string EventHubNameDevices;
        string EventHubNameAlerts;
        string StorageAccountName;
        string WebSiteDirectory;

        // from publish settings
        X509Certificate2 ManagementCertificate;
        string SubscriptionId;

        //--//

        ServiceBusManagementClient sbMgmt;
        string nsConnectionString;
        string storageKey;
        EventHubDescription ehDevices;
        EventHubDescription ehAlerts;

#if AZURESTREAMANALYTICS
        string StreamAnalyticsGroup;
        string JobAggregates;
        string JobAlerts;
#endif

        public bool Run( )
        {
            // Obtain management via .publishsettings file from https://manage.windowsazure.com/publishsettings/index?schemaversion=2.0
            var creds = new CertificateCloudCredentials( SubscriptionId, ManagementCertificate );

            if(!CreateEventHub( creds ))
            {
                return false;
            }

            #region print results

            Console.WriteLine( );
            Console.WriteLine( "Service Bus management connection string (i.e. for use in Service Bus Explorer):" );
            Console.WriteLine( nsConnectionString );
            Console.WriteLine( );
            Console.WriteLine( "Device AMQP address strings (for Raspberry PI/devices):" );
            for ( int i = 1; i <= 4; i++ )
            {
                var deviceKeyName = String.Format( "D{0}", i );
                var deviceKey = ( ehDevices.Authorization.First( ( d )
                        => String.Equals( d.KeyName, deviceKeyName, StringComparison.InvariantCultureIgnoreCase ) ) as SharedAccessAuthorizationRule ).PrimaryKey;

                Console.WriteLine( "amqps://{0}:{1}@{2}.servicebus.windows.net", deviceKeyName, Uri.EscapeDataString( deviceKey ), SBNamespace );

                //Console.WriteLine(new ServiceBusConnectionStringBuilder(nsConnectionString)
                //{
                //    SharedAccessKeyName = deviceKeyName,
                //    SharedAccessKey = deviceKey,
                //}.ToString());
            }

            #endregion

#if AZURESTREAMANALYTICS
            // Create StreamAnalyticsJobs + inputs + outputs + enter keys

            // Untested code. May require AAD authentication, no support for management cert?

            // Create Resource Group for the Stream Analytics jobs
            var groupCreateRequest = WebRequest.Create(String.Format("https://management.azure.com/subscriptions/{0}/resourcegroups/{1}?api-version=2014-04-01-preview",
                SubscriptionId, StreamAnalyticsGroup)) as HttpWebRequest;

            groupCreateRequest.ClientCertificates.Add(creds.ManagementCertificate);
            groupCreateRequest.ContentType = "application/json";
            groupCreateRequest.Method = "PUT";
            groupCreateRequest.KeepAlive = true;

            var bytesGroup = Encoding.UTF8.GetBytes("{\"location\":\"Central US\"}");
            groupCreateRequest.ContentLength = bytesGroup.Length;
            groupCreateRequest.GetRequestStream().Write(bytesGroup, 0, bytesGroup.Length);

            var groupCreateResponse = groupCreateRequest.GetResponse();

            //var streamMgmt = new ManagementClient(creds); //, new Uri("https://management.azure.com"));
            //HttpClient client = streamMgmt.HttpClient;
            
            var createJob = new StreamAnalyticsJob()
            {
                location = Location,
                inputs = new List<StreamAnalyticsEntity> 
                {
                    new StreamAnalyticsEntity 
                    {
                        name = "devicesInput",
                        properties = new Dictionary<string,object>
                        {
                            { "type" , "stream" },
                            { "serialization" , new Dictionary<string,object>
                                {
                                    { "type", "JSON"},
                                    { "properties", new Dictionary<string, object>
                                        {
                                            { "encoding", "UTF8"},
                                        }
                                    }
                                }
                            },
                            { "datasource", new Dictionary<string,object>
                                {
                                    { "type", "Microsoft.ServiceBus/EventHub" },
                                    { "properties", new Dictionary<string,object>
                                        {
                                            { "eventHubNamespace", Namespace },
                                            { "eventHubName", EventHubDevices },
                                            { "sharedAccessPolicyName", "StreamingAnalytics" },
                                            { "sharedAccessPolicyKey", 
                                                (ehDevices.Authorization.First( (d) 
                                                    => String.Equals(d.KeyName, "StreamingAnalytics", StringComparison.InvariantCultureIgnoreCase)) as SharedAccessAuthorizationRule).PrimaryKey },
                                        }
                                    }
                                }
                             }
                        },
                    },
                },
                transformation = new StreamAnalyticsEntity()
                {
                    name = "Aggregates",
                    properties = new Dictionary<string,object>
                    {
                        { "streamingUnits", 1 },
                        { "query" , "select * from devicesInput" },
                    }
                },
                outputs = new List<StreamAnalyticsEntity> 
                {
                    new StreamAnalyticsEntity 
                    {
                        name = "output",
                        properties = new Dictionary<string,object>
                        {
                            { "datasource", new Dictionary<string,object>
                                {
                                    { "type", "Microsoft.ServiceBus/EventHub" },
                                    { "properties", new Dictionary<string,object>
                                        {
                                            { "eventHubNamespace", Namespace },
                                            { "eventHubName", EventHubAlerts },
                                            { "sharedAccessPolicyName", "StreamingAnalytics" },
                                            { "sharedAccessPolicyKey", 
                                                (ehAlerts.Authorization.First( (d) => String.Equals(d.KeyName, "StreamingAnalytics", StringComparison.InvariantCultureIgnoreCase)) as SharedAccessAuthorizationRule).PrimaryKey },
                                        }
                                    }
                                }
                            },
                            { "serialization" , new Dictionary<string,object>
                                {
                                    { "type", "JSON"},
                                    { "properties", new Dictionary<string, object>
                                        {
                                            { "encoding", "UTF8"},
                                        }
                                    }
                                }
                            },
                        },
                    },
                }
            };



            var jobCreateRequest = WebRequest.Create(String.Format("https://management.azure.com/subscriptions/{0}/resourcegroups/{1}/Microsoft.StreamAnalytics/streamingjobs/{2}?api-version=2014-10-01",
                SubscriptionId, StreamAnalyticsGroup, JobAggregates)) as HttpWebRequest;

            jobCreateRequest.ClientCertificates.Add(creds.ManagementCertificate);
            jobCreateRequest.ContentType = "application/json";
            jobCreateRequest.Method = "PUT";
            jobCreateRequest.KeepAlive = true;

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(createJob));
            jobCreateRequest.ContentLength = bytes.Length;
            jobCreateRequest.GetRequestStream().Write(bytes, 0, bytes.Length);

            var jobCreateResponse = jobCreateRequest.GetResponse();

            //var jobCreateTask = streamMgmt.HttpClient.PutAsync(
            //    String.Format("https://management.azure.com/subscriptions/{0}/resourcegroups/{1}/Microsoft.StreamAnalytics/streamingjobs/{2}?api-version=2014-10-01",
            //    SubscriptionId, StreamAnalyticsGroup, JobAggregates),
            //    new StringContent(JsonConvert.SerializeObject(createJob)));
            //jobCreateTask.Wait();
            //var jobCreateResponse = jobCreateTask.Result;
#endif
            return true;
        }

        private bool CreateEventHub( CertificateCloudCredentials creds )
        {
            // Create Namespace
            sbMgmt = new ServiceBusManagementClient( creds );

            ServiceBusNamespaceResponse nsResponse = null;

            Console.WriteLine( "Creating Service Bus namespace {0} in location {1}", SBNamespace, Location );

            try
            {
                // There is (currently) no clean error code returned when the namespace already exists
                // Check if it does
                nsResponse = sbMgmt.Namespaces.Create( SBNamespace, Location );
                Console.WriteLine("Service Bus namespace {0} created.", SBNamespace);
            }
            catch ( Exception )
            {
                nsResponse = null;
                Console.WriteLine("Service Bus namespace {0} already existed.", SBNamespace);
            }

            // Wait until the namespace is active
            while( nsResponse == null || nsResponse.Namespace.Status != "Active" )
            {
                nsResponse = sbMgmt.Namespaces.Get( SBNamespace );
                if( nsResponse.Namespace.Status == "Active" )
                {
                    break;
                }
                Console.WriteLine( "Namespace {0} in state {1}. Waiting...", SBNamespace, nsResponse.Namespace.Status );
                System.Threading.Thread.Sleep( 5000 );
            }

            // Get the namespace connection string 
            var nsDescription = sbMgmt.Namespaces.GetNamespaceDescription( SBNamespace );
            nsConnectionString = nsDescription.NamespaceDescriptions.First(
                ( d ) => String.Equals( d.AuthorizationType, "SharedAccessAuthorization" )
                ).ConnectionString;

            // Create EHs + device keys + consumer keys (WebSite*)
            var nsManager = NamespaceManager.CreateFromConnectionString( nsConnectionString );

            var ehDescriptionDevices = new EventHubDescription( EventHubNameDevices )
            {
                PartitionCount = 8,
            };
            ehDescriptionDevices.Authorization.Add( new SharedAccessAuthorizationRule( "D1", new List<AccessRights> { AccessRights.Send } ) );
            ehDescriptionDevices.Authorization.Add( new SharedAccessAuthorizationRule( "D2", new List<AccessRights> { AccessRights.Send } ) );
            ehDescriptionDevices.Authorization.Add( new SharedAccessAuthorizationRule( "D3", new List<AccessRights> { AccessRights.Send } ) );
            ehDescriptionDevices.Authorization.Add( new SharedAccessAuthorizationRule( "D4", new List<AccessRights> { AccessRights.Send } ) );

            ehDescriptionDevices.Authorization.Add( new SharedAccessAuthorizationRule( "WebSite", new List<AccessRights> { AccessRights.Manage, AccessRights.Listen, AccessRights.Send } ) );

            ehDescriptionDevices.Authorization.Add( new SharedAccessAuthorizationRule( "StreamingAnalytics", new List<AccessRights> { AccessRights.Manage, AccessRights.Listen, AccessRights.Send } ) );

            Console.WriteLine( "Creating Event Hub {0}", EventHubNameDevices );

            EventHubDescription hub = null;
            do
            {
                try
                {
                    hub = nsManager.CreateEventHubIfNotExists( ehDescriptionDevices );
                }
                catch ( System.UnauthorizedAccessException )
                {
                    Console.WriteLine( "Service Bus connection string not valid yet. Waiting..." );
                    System.Threading.Thread.Sleep( 5000 );
                }
            } while( hub == null );

            ehDevices = hub;

            var ehDescriptionAlerts = new EventHubDescription( EventHubNameAlerts )
            {
                PartitionCount = 8,
            };
            ehDescriptionAlerts.Authorization.Add( new SharedAccessAuthorizationRule( "WebSite", new List<AccessRights> { AccessRights.Manage, AccessRights.Listen, AccessRights.Send } ) );
            ehDescriptionAlerts.Authorization.Add( new SharedAccessAuthorizationRule( "StreamingAnalytics", new List<AccessRights> { AccessRights.Manage, AccessRights.Listen, AccessRights.Send } ) );

            Console.WriteLine( "Creating Event Hub {0}", EventHubNameAlerts );

            // Create Storage Account for Event Hub Processor
            var stgMgmt = new StorageManagementClient( creds );
            try
            {
                Console.WriteLine( "Creating Storage Account {0} in location {1}", StorageAccountName, Location );
                var resultStg = stgMgmt.StorageAccounts.Create(
                    new StorageAccountCreateParameters { Name = StorageAccountName.ToLowerInvariant( ), Location = Location, AccountType = "Standard_LRS" } );

                if( resultStg.StatusCode != System.Net.HttpStatusCode.OK )
                {
                    Console.WriteLine( "Error creating storage account {0} in Location {1}: {2}", StorageAccountName, Location, resultStg.StatusCode );
                    return false;
                }
            }
            catch ( CloudException ce )
            {
                if( String.Equals( ce.ErrorCode, "ConflictError", StringComparison.InvariantCultureIgnoreCase ) )
                {
                    Console.WriteLine( "Storage account {0} already existed.", StorageAccountName );
                }
                else
                {
                    throw;
                }
            }

            return true;
        }

#if AZURESTREAMANALYTICS
      
        class StreamAnalyticsEntity
        {
            public string name;
            public Dictionary<string, object> properties;
        }
        class StreamAnalyticsJob
        {
            public string location;
            public Dictionary<string, object> properties;
            public List<StreamAnalyticsEntity> inputs;
            public StreamAnalyticsEntity transformation;
            public List<StreamAnalyticsEntity> outputs;
        }
#endif

        static int Main( string[] args )
        {
            var p = new Program( );

            bool result = p.Parse( args ); 

            if( result )
            {
                try
                {
                    result = p.Run( );
                }
                catch ( Exception e )
                {
                    Console.WriteLine( "Exception {0} while creating Azure resources at {1}", e.Message, e.StackTrace );
                }
            }

            return result ? 0 : 1;
        }

        private bool Parse( string[] args )
        {
            bool bParseError = false;
            for ( int i = 0; i < args.Length; i++ )
            {
                switch ( args[ i ].Substring( 0, 1 ).Replace( "/", "-" ) + args[ i ].Substring( 1 ).ToLowerInvariant( ) )
                {
                    case "-nameprefix":
                    case "-n":
                        i++;
                        if( i < args.Length )
                        {
                            NamePrefix = args[ i ];
                        }
                        else
                        {
                            Console.WriteLine( "Error: missing NamePrefix argument" );
                            bParseError = true;
                        }
                        break;
                    case "-location":
                    case "-l":
                        i++;
                        if( i < args.Length )
                        {
                            Location = args[ i ];
                        }
                        else
                        {
                            Console.WriteLine( "Error: missing Location argument" );
                            bParseError = true;
                        }
                        break;
                    case "-publishsettingsfile":
                    case "-ps":
                        try
                        {
                            i++;
                            if( i >= args.Length )
                            {
                                Console.WriteLine( "Error: missing NamePrefix argument" );
                                bParseError = true;
                            }
                            else
                            {

                                var doc = new XmlDocument( );
                                doc.Load( args[ i ] );
                                var certNode =
                                    doc.SelectSingleNode(
                                        "/PublishData/PublishProfile/@ManagementCertificate" );
                                // Some publishsettings files (with multiple subscriptions?) have the management publisherCertificate under the Subscription
                                if( certNode == null )
                                {
                                    certNode =
                                    doc.SelectSingleNode(
                                        "/PublishData/PublishProfile/Subscription/@ManagementCertificate" );
                                }
                                ManagementCertificate = new X509Certificate2( Convert.FromBase64String( certNode.Value ) );
                                var subNode =
                                    doc.SelectSingleNode( "/PublishData/PublishProfile/Subscription/@Id" );
                                SubscriptionId = subNode.Value;
                            }
                        }
                        catch ( Exception exception )
                        {
                            Console.WriteLine( "Error: invalid publishsettings file - {0}", exception.Message );
                            bParseError = true;
                        }
                        break;
                    default:
                        Console.WriteLine( "Error: unrecognized argument: {0}", args[ i ] );
                        bParseError = true;
                        break;
                }
                if( bParseError )
                {
                    break;
                }
            }

            if( bParseError )
            {
                Console.WriteLine( "Usage: ConnectTheDotsAzurePrep -PublishSettingsFile <settingsfile> [-NamePrefix <prefix>] [-Location <location>] [-website <websitedir>]" );
                return false;
            }

            if( NamePrefix == null )
            {
                NamePrefix = "IoTDemo" + Guid.NewGuid( ).ToString( "N" ).GetHashCode( ).ToString( "x" );
            }
            if( Location == null )
            {
                Location = "Central US";
            }
            if( SBNamespace == null )
            {
                SBNamespace = NamePrefix + "-ns";
            }
            if( StorageAccountName == null )
            {
                StorageAccountName = NamePrefix.ToLowerInvariant( ) + "storage";
            }
            if( EventHubNameDevices == null )
            {
                EventHubNameDevices = "ehdevices";
            }
            if( EventHubNameAlerts == null )
            {
                EventHubNameAlerts = "ehalerts";
            }
            if( WebSiteDirectory == null )
            {
                WebSiteDirectory = "..\\..\\..\\..\\WebSite\\ConnectTheDotsWebSite"; // Default for running the tool from the bin/debug or bin/release directory (i.e within VS)
            }

#if AZURESTREAMANALYTICS
            StreamAnalyticsGroup = NamePrefix + "-StreamAnalytics";
            JobAggregates = NamePrefix + "-aggregates";
            JobAlerts = NamePrefix + "-alerts";
#endif


            return true;
        }

    }
}