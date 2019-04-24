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
    
    const extractedGit: StaticDirectory = importFrom("git").extracted;
    const extractedRush: StaticDirectory = importFrom("rush").extracted;
    const rushExec: File = extractedRush.getFile(r`rush/node_modules/@microsoft/rush/bin/rush`);
    
    let path: string = "";
    let toolDependencies: StaticDirectory[] = [];
    
    if(Context.getCurrentHost().os === "win") {
      const winNode: StaticDirectory = importFrom("NodeJs.win-x64").extracted;
      
      path = [
        rushExec.parent,
        d`${extractedGit.path}/git`,
        d`${winNode.path}/node-v10.15.3-win-x64`,
        d`${winNode.path}/node-v10.15.3-win-x64/node_modules/npm/bin`,
      ].map(d => d.toDiagnosticString()).join(";");
    } else {
      const macNode: StaticDirectory = importFrom("NodeJs.osx-x64").extracted;
      const macNpm: StaticDirectory = importFrom("npm.osx").extracted;
      
      path = [
        rushExec.parent,
        d`${extractedGit.path}/git`,
        d`${macNode.path}/node-v10.15.3-darwin-x64/bin`,
        d`${macNpm.path}/npm/node_modules/npm/bin`,
        d`/bin`,
        d`/usr/bin`,
      ].map(d => d.toDiagnosticString()).join(":");
      
      toolDependencies = [
        macNpm
      ];
    }
    
    return Node.run({
      arguments: [
        Cmd.argument(Artifact.input(rushExec)),
        Cmd.args(cmdArgs)
      ],
      workingDirectory: wd,
      dependencies: [
        extractedGit,
        extractedRush,
        ...toolDependencies,
        ...rushArgs.dependencies
      ],
      outputs: [
        wd,
        ...rushArgs.outputs
      ],
      environmentVariables: [
        { name: "PATH", value: path },
        { name: "HOME", value: wd.toDiagnosticString() },
        { name: "APPDATA", value: wd.toDiagnosticString() },
        { name: "USERPROFILE", value: wd.toDiagnosticString() },
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
          ...( gitDirPath ? [ d`${gitDirPath}` ] : []),
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
