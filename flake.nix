{
  description = "No game engines???";
  inputs = {
    flake-utils.url = "github:numtide/flake-utils";
    nixpkgs.url = "nixpkgs/nixos-unstable";
    nuget-packageslock2nix = {
      url = "github:mdarocha/nuget-packageslock2nix/main";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };
  outputs = { nixpkgs, flake-utils, nuget-packageslock2nix, ...}:
    flake-utils.lib.eachDefaultSystem (system:
      let 
        pkgs = import nixpkgs { inherit system; };
        dotnetPackage = pkgs.dotnet-sdk_8;
        deps = with pkgs; [
          dotnetPackage
          SDL2
          SDL2_gfx
          SDL2_image
          SDL2_mixer
          SDL2_ttf

          gcc
          pkg-config
        ];
      in
      {
        defaultPackage = pkgs.buildDotnetModule rec {
          pname = "engineless";
          version = "0.0.1";
          src = ./.;
          dotnet-sdk = dotnetPackage;
          runtimeDeps = deps;
          nugetDeps = nuget-packageslock2nix.lib {
            system = system;
            name = pname;
            lockfiles = [
              ./packages.lock.json
            ];
          };
        };
        devShell = pkgs.mkShell { packages = deps; };
      }
    );
}
