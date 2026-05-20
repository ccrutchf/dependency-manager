{
  description = "depend — declarative package manager for Linux";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachSystem [ "x86_64-linux" "aarch64-linux" ] (system:
      let
        pkgs = import nixpkgs { inherit system; };
        dotnet = pkgs.dotnetCorePackages.sdk_10_0;

        depend = pkgs.buildDotnetModule {
          pname = "depend";
          version = "0.0.0";

          src = ./.;

          projectFile = "src/DependencyManager/DependencyManager.csproj";
          testProjectFile = "tests/DependencyManager.Tests/DependencyManager.Tests.csproj";

          # Run `nix build .#depend.fetch-deps` and execute the resulting script
          # to (re)generate this file whenever NuGet references change.
          nugetDeps = ./nix/deps.json;

          dotnet-sdk = dotnet;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;

          executables = [ "depend" ];

          selfContainedBuild = true;

          meta = {
            description = "Declarative package manager that drives apt, snap, flatpak, pip, pipx, cargo, nvm and scripts from a YAML manifest";
            homepage = "https://github.com/ccrutchf/dependency-manager";
            mainProgram = "depend";
          };
        };
      in
      {
        packages = {
          default = depend;
          depend = depend;
        };

        apps.default = {
          type = "app";
          program = "${depend}/bin/depend";
        };

        devShells.default = pkgs.mkShell {
          packages = [
            dotnet
            pkgs.git
          ];

          DOTNET_ROOT = "${dotnet}";
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
          DOTNET_NOLOGO = "1";
        };

        formatter = pkgs.nixpkgs-fmt;
      });
}
