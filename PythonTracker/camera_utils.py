from dataclasses import dataclass
from typing import Callable, Protocol


class Frame(Protocol):
    pass


class CameraCapture(Protocol):
    def isOpened(self) -> bool: ...
    def read(self) -> tuple[bool, Frame]: ...
    def release(self) -> None: ...


@dataclass(frozen=True, slots=True)
class CameraDevice:
    index: int
    available: bool
    reason: str = ""


CaptureFactory = Callable[[int], CameraCapture]


@dataclass(frozen=True, slots=True)
class CameraUnavailableError(Exception):
    index: int
    reason: str

    def __str__(self) -> str:
        return f"camera index {self.index} is unavailable ({self.reason})"


def discover_cameras(factory: CaptureFactory, max_index: int = 10) -> list[CameraDevice]:
    """Probe bounded indices and retain only devices that open and yield a frame."""
    devices: list[CameraDevice] = []
    for index in range(max(0, max_index)):
        capture = factory(index)
        try:
            if not capture.isOpened():
                continue
            ok, _frame = capture.read()
            if ok:
                devices.append(CameraDevice(index=index, available=True))
        finally:
            capture.release()
    return devices


def open_selected_camera(factory: CaptureFactory, index: int) -> CameraCapture:
    """Open exactly ``index`` and reject it when open or first frame read fails."""
    capture = factory(index)
    if not capture.isOpened():
        capture.release()
        raise CameraUnavailableError(index=index, reason="open failed")
    ok, _frame = capture.read()
    if not ok:
        capture.release()
        raise CameraUnavailableError(index=index, reason="frame read failed")
    return capture
