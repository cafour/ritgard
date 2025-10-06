from datetime import datetime, UTC

def get_now_string():
    return datetime.now(UTC).strftime("%Y-%m-%d_%H-%M-%S")