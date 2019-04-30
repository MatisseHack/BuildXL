import {Artifact, Cmd, Transformer} from "Sdk.Transformers";

/**
 * npx downloads and executes an npm module in one command,
 * which makes it easier to use a tool not installed on the system.
 * Use this tool to make it easier to prototype an npm tool wrapper.
 */
namespace Npx {
  /**
   * Calls the target package via npx via the Node tool.
   * 
   * @param args The {@link Arguments} object to use for execution
   */
  @@public
  export function run(args: Arguments) : Transformer.ExecuteResult {
    args = {
      arguments: [
        // Everything passed in a single Cmd.args since using Cmd.flag will pull that flag to the beginning of the invocation and cause the command to fail
        // (i.e. it should be node npx -p [package] not node -p [package] npx).
        Cmd.args([
          // Pass in the npx executable and mark it as an input so that it can be read from
          Artifact.input(getNpx()),
          "--cache",
          // mark the temp npm cache as an output so that it can be written to
          Artifact.output(d`${args.workingDirectory}/npm-cache`),
          "-p",
          args.packageVersion ? `${args.packageName}@${args.packageVersion}` : args.packageName,
          args.packageName
        ]),
      ],
      // These were copied from the Npm tool. Some of them might not be needed now.
      environmentVariables: [
        { name: "NPM_CONFIG_USERCONFIG", value: f`${args.workingDirectory}/.npmrc` }, // Prevents user configuration to change behavior
        { name: "NPM_CONFIG_GLOBALCONFIG", value: f`${args.workingDirectory}/global.npmrc` }, // Prevent machine installed configuration file to change behavior.
        { name: "NO_UPDATE_NOTIFIER", value: "1" }, // Prevent npm from checking for the latest version online and write to the user folder with the check information
      ],
      unsafe: {
        untrackedPaths: [
          // Uses the special "UserProfile" mount, which points to HOME when run on a Mac
          f`${Context.getMount("UserProfile").path}/.CFUserTextEncoding`
        ]
      }
    }.merge(args); // Merges the two objects, but order and undefined properties matter
    
    return Node.run(args);
  }
  
  /**
   * Get the downloaded npx executable depending on the platform
   */
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
    /**
     * The name of the npm package to download and run with npx.
     */
    packageName: string,

    /**
     * The version of the package to use. By default, npx will download the latest version if no other version is specified.
     */
    packageVersion?: string
  }
}
