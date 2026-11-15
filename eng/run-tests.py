"""Runs the test suite in CI with a time limit and one more try when it hangs.

On the hosted macOS runners the test host now and then starts and then prints nothing at all, before any test runs,
until the job's time limit. That is no failing test, so --blame-hang-timeout does not catch it. This script gives each
run a time limit; when it passes, the whole process tree is stopped and the suite runs once more. A suite that fails
by itself is not run again: a failing test stays red.

    python eng/run-tests.py --minutes 20 -- dotnet test Jourfold.sln -c Release --no-build ...
"""

import argparse
import os
import signal
import subprocess
import sys
import time


def stop(process: subprocess.Popen) -> None:
    """Stops the process and everything it started, such as the test host."""
    if os.name == "nt":
        subprocess.run(["taskkill", "/T", "/F", "/PID", str(process.pid)], capture_output=True, check=False)
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait()


def attempt(command: list[str], minutes: float) -> int | None:
    """Runs the command once. Returns its exit code, or None when it ran out of time."""
    started = time.monotonic()
    process = subprocess.Popen(command, start_new_session=os.name != "nt")
    try:
        return process.wait(timeout=minutes * 60)
    except subprocess.TimeoutExpired:
        print(f"::warning::The tests gave no result within {minutes:g} minutes "
              f"(after {(time.monotonic() - started) / 60:.1f} min); stopping them.", flush=True)
        stop(process)
        return None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--minutes", type=float, default=20, help="time limit for one run of the suite")
    parser.add_argument("--tries", type=int, default=2, help="how often a run that ran out of time is started")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="the test command, after --")
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("no test command given")
    for number in range(1, args.tries + 1):
        if number > 1:
            print(f"::warning::Running the tests again (try {number} of {args.tries}) after a run that hung.", flush=True)
        code = attempt(command, args.minutes)
        if code is not None:
            return code
    print(f"::error::The tests hung {args.tries} times; see the test output above for the last test that started.", flush=True)
    return 1


if __name__ == "__main__":
    sys.exit(main())
