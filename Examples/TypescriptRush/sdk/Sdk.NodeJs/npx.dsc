import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

namespace Npx {
  @@public
  export function run(args: Arguments) : Transformer.ExecuteResult {
    args = {
      arguments: [
        Cmd.args([
          Artifact.input(getNpx()),
          "--cache",
          Artifact.output(d`${args.workingDirectory}/npm-cache`),
          "-p",
          args.packageVersion ? `${args.packageName}@${args.packageVersion}` : args.packageName,
          args.packageName
        ]),
      ],
      environmentVariables: [
        { name: "NPM_CONFIG_USERCONFIG", value: f`${args.workingDirectory}/.npmrc` }, // Prevents user configuration to change behavior
        { name: "NPM_CONFIG_GLOBALCONFIG", value: f`${args.workingDirectory}/global.npmrc` }, // Prevent machine installed configuration file to change behavior.
        { name: "NO_UPDATE_NOTIFIER", value: "1" }, // Prevent npm from checking for the latest version online and write to the user folder with the check information
      ],
      unsafe: {
        untrackedPaths: [
          f`${Context.getMount("UserProfile").path}/.CFUserTextEncoding`
        ]
      }
    }.merge(args);
    
    return Node.run(args);
  }
  
  function getNpx() {
    const host = Context.getCurrentHost();
    
    Contract.assert(host.cpuArchitecture === "x64", "Only 64bit verisons supported.");
    
    let executable : RelativePath = undefined;
    let pkgContents : StaticDirectory = undefined;
    
    switch (host.os) {
      case "win":
      pkgContents = importFrom("NodeJs.win-x64").extracted;
      executable = r`node-v10.15.3-win-x64/node_modules/npm/bin/npx-cli.js`;
      break;
      case "macOS":
      pkgContents = importFrom("NodeJs.osx-x64").extracted;
      executable = r`node-v10.15.3-darwin-x64/lib/node_modules/npm/bin/npx-cli.js`;
      break;
      case "unix":
      pkgContents = importFrom("NodeJs.linux-x64").extracted;
      executable = r`node-v10.15.3-linux-arm64/lib/node_modules/npm/bin/npx-cli.js`;
      break;
      default:
      Contract.fail(`The current NodeJs package doesn't support the current OS: ${host.os}. Ensure you run on a supported OS -or- update the NodeJs package to have the version embedded.`);
    }
    
    return pkgContents.getFile(executable);
  }
  
  interface Arguments extends Transformer.ExecuteArguments {
    packageName: string,
    packageVersion?: string
  }
}
