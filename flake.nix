{
  description = "MailMerge .NET development environment";
  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  outputs = { self, nixpkgs }:
    let
      supportedSystems =
        [ "x86_64-linux" "aarch64-linux" "x86_64-darwin" "aarch64-darwin" ];
      forEachSupportedSystem = f:
        nixpkgs.lib.genAttrs supportedSystems (system:
          f {
            pkgs = import nixpkgs {
              inherit system;
              config = { allowUnfree = true; };
            };
          });
    in {
      devShells = forEachSupportedSystem ({ pkgs }: {
        default = pkgs.mkShell {
          packages = with pkgs; [
            (with dotnetCorePackages; combinePackages [ sdk_10_0 ])
            csharp-ls
          ];

          shellHook = ''
            mkdir -p "$HOME/.nuget/NuGet"
            export DOTNET_ROOT="${pkgs.dotnetCorePackages.sdk_10_0}"
            export DOTNET_CLI_TELEMETRY_OPTOUT=1
            export DOTNET_NOLOGO=1
            export PATH="$HOME/.dotnet/tools:$PATH"
          '';
        };
      });
    };
}
