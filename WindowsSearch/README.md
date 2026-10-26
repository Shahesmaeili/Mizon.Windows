# Mizon Windows Search Provider

This NativeAOT DLL exposes one hard-coded item:

`mizon://music/0195dd3c-9a3e-7c4a-8c6f-2e9c0f6b1a72`

It has no desktop or Start Menu shortcut and no API/JWT dependency. Its COM entry points are exported directly so Windows Search does not load CoreCLR.
