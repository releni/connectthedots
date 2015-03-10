#  ---------------------------------------------------------------------------------
#  Copyright (c) Microsoft Open Technologies, Inc.  All rights reserved.
# 
#  The MIT License (MIT)
# 
#  Permission is hereby granted, free of charge, to any person obtaining a copy
#  of this software and associated documentation files (the "Software"), to deal
#  in the Software without restriction, including without limitation the rights
#  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
#  copies of the Software, and to permit persons to whom the Software is
#  furnished to do so, subject to the following conditions:
# 
#  The above copyright notice and this permission notice shall be included in
#  all copies or substantial portions of the Software.
# 
#  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
#  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
#  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
#  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
#  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
#  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
#  THE SOFTWARE.
#  ---------------------------------------------------------------------------------
#!/bin/bash

export GW_HOME=~/GatewayService
export LOGS=$GW_HOME/logs

#
# event log entries will be written to /var/lib/mono/EventLog/Application
#
echo "Setting MONO_EVENTLOG_TYPE to local"
export MONO_EVENTLOG_TYPE=local
#
# Start monitoring gateway process
#
echo "Monitoring Gateway"
LOG=monitor_$(date +"%m-%d-%Y-%T").log
MONITORED="GatewayService"
PERIOD=5
DELETE_LOCK="sudo rm -f /tmp/Microsoft.ConnectTheDots.GatewayService.exe.lock"
RESTART="/usr/bin/mono-service $GW_HOME/Microsoft.ConnectTheDots.GatewayService.exe"

cd $GW_HOME

while :
do
	 test `ps ax | grep $MONITORED | awk '{ print $1;}' | wc | awk '{ print $1;}'` -gt 1 && RUNNING=1 || RUNNING=0
	 test $RUNNING -eq 0 && echo "Restarting..." && $DELETE_LOCK && $RESTART || echo "$MONITORED is running..." >> $LOGS/$LOG
	 sleep $PERIOD
done