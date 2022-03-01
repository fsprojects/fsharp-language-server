{ pkgs ? import <nixos-unstable> {} }:

let
  buildDotnet = with pkgs.dotnetCorePackages; combinePackages [
    sdk_6_0
  ];
  in
pkgs.mkShell {
  buildInputs = [
    pkgs.hello
    pkgs.openssl.dev
    pkgs.openssl
    pkgs.openssl.out
    pkgs.pkg-config
    # keep this line if you use bash
    pkgs.bashInteractive
    pkgs.nodejs
    pkgs.nodePackages.npm
    
  ];
  nativeBuildInputs=[
    buildDotnet
    pkgs.openssl
    pkgs.openssl.dev
    pkgs.openssl.out
    pkgs.pkg-config
  ];
}


