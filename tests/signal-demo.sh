#!/usr/bin/env bash
# Signal demo target for the Processes page.
# Prints a line every second so you can watch it react to the app's actions.
#
#   Suspend  (SIGSTOP) -> output freezes. SIGSTOP cannot be caught, so no message.
#   Resume   (SIGCONT) -> prints "RESUMED" and the ticks continue.
#   Priority (nice)    -> the "nice=" value on each line changes.
#   Terminate(SIGTERM) -> prints a cleanup message, then exits.
#   Force kill(SIGKILL)-> dies instantly, prints nothing (cannot be caught).

echo "signal-demo running. PID = $$  (search for 'signal-demo' in the app)"

trap 'echo "$(date +%T)  >>> SIGCONT received: RESUMED"' CONT
trap 'echo "$(date +%T)  >>> SIGTERM received: cleaning up and exiting politely"; exit 0' TERM

tick=0
while true; do
    tick=$((tick + 1))
    nice_value=$(ps -o ni= -p $$ | tr -d ' ')
    echo "$(date +%T)  tick $tick   pid=$$   nice=$nice_value"
    sleep 1
done
