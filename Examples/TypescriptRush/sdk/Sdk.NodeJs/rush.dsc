import { Artifact, Cmd, Transformer } from 'Sdk.Transformers';

namespace Rush {
  @@public
  export function install(args: RushArguments): Transformer.ExecuteResult {
    return run(["install"], args);
  }
  
  @@public
  export function build(args: RushBuildArguments): Transformer.ExecuteResult {
    return run([ "build", "-t", args.target ], args);
  }
  
  function run(cmdArgs: string[], rushArgs: RushArguments) {
    const wd = Context.getNewOutputDirectory("rush-" + cmdArgs[0]);
    const gitDirPath = searchUp(Context.getMount("SourceRoot").path, a`.git`);

    const extractedNode : StaticDirectory = importFrom("NodeJs.osx-x64").extracted;
    const extractedNpm : StaticDirectory = importFrom("npm.osx").extracted;
    const extractedGit : StaticDirectory = importFrom("git.osx").extracted;
    const extractedRush : StaticDirectory = importFrom("rush.osx").extracted;
    const rushExec : File = extractedRush.getFile(r`rush/node_modules/@microsoft/rush/bin/rush`);
    
    const path = [
      d`${extractedNode.path}/node-v10.15.3-darwin-x64/bin`,
      d`${extractedNpm.path}/npm/node_modules/npm/bin`,
      d`${extractedGit.path}/git`,
      rushExec.parent,
      d`/bin`,
      d`/usr/bin`,
    ].map(d => d.toDiagnosticString()).join(":");
    
    return Node.run({
      arguments: [
        Cmd.argument(Artifact.input(rushExec)),
        Cmd.args(cmdArgs)
      ],
      workingDirectory: wd,
      dependencies: [
        extractedNpm,
        extractedGit,
        extractedRush,
        ...rushArgs.dependencies
      ],
      outputs: [
        wd,
        ...rushArgs.outputs
      ],
      environmentVariables: [
        { name: "PATH", value: path },
        { name: "HOME", value: wd.toDiagnosticString() },
      ],
      unsafe: {
        untrackedScopes: [
          d`/bin`,
          d`/dev`,
          d`/etc`,
          d`/private`,
          d`/System/Library`,
          d`/usr`,
          d`/var`,
          d`${gitDirPath}`,
          ...(rushArgs.untrackedScopes || [])
        ],
        untrackedPaths: [
          f`${Context.getMount("UserProfile").path}/.CFUserTextEncoding`,
          ...(rushArgs.untrackedPaths || [])
        ]
      }
    });
  }
  
  function searchUp(start: Path, search: PathAtom) : Path {
    while (start.hasParent) {
      let candidate = p`${start}/${search}`;
      if (Directory.exists(d`${candidate}`) || File.exists(f`${candidate}`)) {
        return candidate;
      }
      start = start.parent;
    }
  }
  
  interface RushArguments extends Transformer.RunnerArguments {
    dependencies: Transformer.InputArtifact[],
    outputs: Transformer.Output[],
    untrackedPaths?: (File | Directory)[],
    untrackedScopes?: Directory[]
  }
  
  interface RushBuildArguments extends RushArguments {
    target: string
  }
}
