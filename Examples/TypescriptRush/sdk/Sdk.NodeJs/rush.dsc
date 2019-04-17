import { Artifact, Cmd, Transformer } from 'Sdk.Transformers';

namespace Rush {
  const home = Context.getMount("UserProfile").path;
  const gitDirPath = searchUp(Context.getMount("SourceRoot").path, a`.git`);
  
  const unsafe: Transformer.UnsafeExecuteArguments = {
    untrackedScopes: [
      d`/usr`,
      d`/private`,
      d`/dev`,
      d`/etc`,
      d`/Library`,
      d`/System/Library`,
      d`/var`,
      d`/bin`,
      d`/Applications/Xcode.app/Contents`,
      d`${home}/.rush`,
      d`${gitDirPath}`,
    ],
    untrackedPaths: [
      f`${home}/.npmrc`,
      f`${home}/.gitconfig`
    ],
    passThroughEnvironmentVariables: [
      // TODO
      "PATH",
      "HOME"
    ]
  };
  
  @@public
  export function install(args: InstallArguments): Transformer.ExecuteResult {
    const wd = Context.getNewOutputDirectory("rush-install");
    return Npx.run({
      description: "install",
      packageName: "rush",
      arguments: [
        Cmd.argument("install")
      ],
      workingDirectory: wd,
      dependencies: args.dependencies,
      outputs: [
        d`${wd}/.rush`,
        ...args.outputs
      ],
      unsafe: { untrackedScopes: args.untrackedScopes, untrackedPaths: args.untrackedPaths }.merge(unsafe)
    });
  }
  
  @@public
  export function build(args: BuildArguments): Transformer.ExecuteResult {
    const wd = Context.getNewOutputDirectory("rush-build");
    return Npx.run({
      description: args.target,
      packageName: "rush",
      arguments: [
        Cmd.args([ "build", "-t", args.target, ">", Artifact.output(p`${wd}/build-out.txt`) ])
      ],
      workingDirectory: wd,
      dependencies: args.dependencies,
      outputs: args.outputs,
      allowUndeclaredSourceReads: true, // rush calls git, which can read any modified file in the repo
      unsafe: { untrackedScopes: args.untrackedScopes, untrackedPaths: args.untrackedPaths }.merge(unsafe)
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
  
  interface InstallArguments extends Transformer.RunnerArguments {
    dependencies: Transformer.InputArtifact[],
    outputs: Transformer.Output[],
    untrackedPaths?: (File | Directory)[],
    untrackedScopes?: Directory[]
  }
  
  interface BuildArguments extends Transformer.RunnerArguments {
    target: string,
    dependencies: Transformer.InputArtifact[],
    outputs: Transformer.Output[],
    untrackedPaths?: (File | Directory)[],
    untrackedScopes?: Directory[]
  }
}
