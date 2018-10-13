#!/bin/bash

set -e

artifact=build.vsix
package=georgewfraser.fsharp-language-server

if [ ! -z `code --list-extensions | grep $package` ]; then
  code --uninstall-extension $package
fi

code --install-extension $artifact
