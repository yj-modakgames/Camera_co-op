import unittest

from camera_utils import CameraUnavailableError, discover_cameras, open_selected_camera


class FakeCapture:
    def __init__(self, opened: bool, readable: bool) -> None:
        self.opened = opened
        self.readable = readable
        self.released = False

    def isOpened(self) -> bool:
        return self.opened

    def read(self) -> tuple[bool, str]:
        return self.readable, "frame"

    def release(self) -> None:
        self.released = True


class CameraUtilsTests(unittest.TestCase):
    def test_discovery_keeps_only_open_and_readable_indices(self) -> None:
        captures = {0: FakeCapture(True, True), 1: FakeCapture(True, False), 2: FakeCapture(False, False)}

        devices = discover_cameras(captures.__getitem__, max_index=3)

        self.assertEqual([device.index for device in devices], [0])
        self.assertTrue(all(capture.released for capture in captures.values()))

    def test_selected_index_does_not_fallback(self) -> None:
        captures = {0: FakeCapture(True, True), 1: FakeCapture(False, False)}

        with self.assertRaisesRegex(CameraUnavailableError, "index 1"):
            open_selected_camera(captures.__getitem__, 1)

        self.assertTrue(captures[1].released)
