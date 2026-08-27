#!/bin/sh
# tracker 실행. 프리뷰 창에서 q 를 누르면 종료된다.
cd "$(dirname "$0")"

if [ ! -x .venv/bin/python ]; then
    echo "Run setup_tracker.sh first."
    exit 1
fi

echo "Starting hand tracker. Press q on the preview window to quit."
exec .venv/bin/python hand_tracker.py
