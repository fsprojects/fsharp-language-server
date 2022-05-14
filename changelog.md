## 0.1.81
- Added keywords to autocomplete
- fixed a few bugs in the path normalization and caching
## 0.1.80
- Using Ionide.projInfo for project cracking which should fix all unessicary rechecks due to building the project
- Added a project cracking caching feature so that projects now load way faster after being opened ap least once.
It's a small list but this update was a pretty big undertaking and has made huge strides in project opening speed.
## 0.1.70
### User facing
- Fixed a problem that meant it was never possible to use recent typechecks. This should massively improve autocomplete and hover speed consistency and reduce rechecking of files.
- Fixed a problem with summary being duplicated in hover docs that was introduced in 0.1.6
- Added support for net-windows, net-macos etc versions of the sdk
### Internal
- Total overhaul of testing, now using expecto, debugging is very easy, CI is working.
- Moved buildalyzer location yet again. Now it is inside /obj
- fixed an occasional bug that would cause some tests to fail because of running in parallel

## 0.1.60
- Added support for any text inside a /// comment appearing in hover tooltips.
- Fixed bug that inserted annoying ** into empty tooltips
- Renamed Buildalyzer artifacts location
## 0.1.51
- New and improved logging
- MangleMaxine added improved grammars
- Fixed Buildalyzer deleting build artifacts
- Added paket
## 0.1.5

Improved signature help and hover for methods in classes. Both now include parameter information and possible exceptions
## 0.1.41
Fixed bug with finding dotnet executable on windows
## 0.1.40
Switched from using binaries to publishing a netcore dependant dll.
    Massively reduces extension size and also reduces problems with running binaries on strange operating systems or not having certain dependencies


## 0.1.32
fixed a few minor tooltip issues, including issue #1
trying out publishing from linux to fix permissions problems


## 0.1.31
Just little maintenance changes to readmes and icons and stuff to differentiate form fsharp language server
