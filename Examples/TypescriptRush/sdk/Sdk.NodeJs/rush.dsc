import { Artifact, Cmd, Transformer } from 'Sdk.Transformers';

namespace Rush {
  /**
   * Calls "rush install" to install all node modules needed by packages.
   * 
   * @param args The {@link RushArguments} object containing dependency and output information for the pip
   */
  @@public
  export function install(args: RushArguments): Transformer.ExecuteResult {
    return run(["install"], args);
  }
  
  /**
   * Calls "rush build -t (target)" to build a specific package.
   * 
   * @param args The {@link RushBuildArguments} object containing dependency and output information for the pip
   */
  @@public
  export function build(args: RushBuildArguments): Transformer.ExecuteResult {
    return run([ "build", "-t", args.target ], args);
  }
  
  /**
   * Runs Rush via the Node tool.
   * 
   * @param cmdArgs The command line arguments to pass to Rush
   * @param rushArgs The {@link RushArguments} object containing dependency and output information for the pip
   */
  function run(cmdArgs: string[], rushArgs: RushArguments) {
    // Get a new temp output dir based on the Rush command passed in ("install" or "build")
    const wd = Context.getNewOutputDirectory("rush-" + cmdArgs[0]);
    // Search for the .git folder and get it's path
    const gitDirPath = searchUp(Context.getMount("SourceRoot").path, a`.git`);
    
    // Get paths to required tools
    const gitDir = d`./bin/git`;
    const rushDir = d`./bin/rush`;
    const rushTool = f`${rushDir}/node_modules/.bin/rush`;
    
    // Variables require initializers in DScript
    let path: string = "";
    let otherToolDependencies: File[] = [];
    
    // Set the path differently if on Windows vs Mac
    if(Context.getCurrentHost().os === "win") {
      // Use the downloaded Windows version of Node
      const winNode: StaticDirectory = importFrom("NodeJs.win-x64").extracted;
      
      // Add node, npm, rush, and git to the PATH
      path = [
        d`${winNode.path}/node-v10.15.3-win-x64`,
        d`${winNode.path}/node-v10.15.3-win-x64/node_modules/npm/bin`,
        rushTool.parent,
        gitDir,
      ].map(d => d.toDiagnosticString()).join(";"); // Uses toDiagnosticString, which is not recommended, but unavoidable. Also notice Windows separator ";"
    } else {
      // Use the downloaded Mac version of Node
      const macNode: StaticDirectory = importFrom("NodeJs.osx-x64").extracted;
      // Node includes npm, but the executables don't work on a mac without symlinking
      // (it is meant for Windows so Macs need to use the "npm-cli.js" file instead).
      // To get around this we have built a custom version of npm for Mac
      // This would also be solved if BuildXL kept symlinks when extracting
      const macNpmDir = d`./bin/npm`;
      const macNpmTool = f`${macNpmDir}/node_modules/.bin/npm`;
      
      // Add node, npm, rush, and git to the PATH along with /bin and /usr/bin
      path = [
        d`${macNode.path}/node-v10.15.3-darwin-x64/bin`,
        macNpmTool.parent,
        rushTool.parent,
        gitDir,
        d`/bin`,
        d`/usr/bin`,
      ].map(d => d.toDiagnosticString()).join(":"); // Uses toDiagnosticString, which is not recommended, but unavoidable. Also notice unix separator ":"
      
      // Also add our custom Mac version of npm to otherToolDependencies so that these files can be included as input
      otherToolDependencies = globR(macNpmDir);
    }
    
    return Node.run({
      arguments: [
        // Pass in the rush executable and mark it as an input so that it can be read from
        Cmd.argument(Artifact.input(rushTool)),
        // The other arguments to be passed to rush
        Cmd.args(cmdArgs)
      ],
      workingDirectory: wd,
      dependencies: [
        // Add tool directories as inputs
        // Globing all the files is not ideal, but only Files and StaticDirectories are allowed
        ...globR(gitDir),
        ...globR(rushDir),
        ...otherToolDependencies,
        // Add the dependencies passed in by the caller
        ...rushArgs.dependencies
      ],
      outputs: [
        // Allow file writes to the working directory
        wd,
        // Add the outputs passed in by the caller
        ...rushArgs.outputs
      ],
      environmentVariables: [
        { name: "PATH", value: path },
        // Only needed on Mac. Uses toDiagnosticString, which is not recommended, but unavoidable.
        { name: "HOME", value: wd.toDiagnosticString() },
        // Only needed on Windows. Uses toDiagnosticString, which is not recommended, but unavoidable.
        { name: "APPDATA", value: wd.toDiagnosticString() },
        // Only needed on Windows. Uses toDiagnosticString, which is not recommended, but unavoidable.
        { name: "USERPROFILE", value: wd.toDiagnosticString() },
      ],
      unsafe: {
        // Allow access to several system scopes that various tools seem to need access to
        untrackedScopes: [
          d`/bin`,
          d`/dev`,
          d`/etc`,
          d`/private`,
          d`/System/Library`,
          d`/usr`,
          d`/var`,
          // Array.push() doesn't work so this line adds the .git folder to the untracked scopes if it is not undefined.
          ...( gitDirPath ? [ d`${gitDirPath}` ] : []),
          // Add any untracked scopes passed in by the caller
          ...(rushArgs.untrackedScopes || [])
        ],
        untrackedPaths: [
          // Uses the special "UserProfile" mount, which points to HOME when run on a Mac
          f`${Context.getMount("UserProfile").path}/.CFUserTextEncoding`,
          // Add any untracked paths passed in by the caller
          ...(rushArgs.untrackedPaths || [])
        ]
      }
    });
  }
  
  /**
   * Searches for a particular file or folder in a directory and it's parent directories.
   * 
   * @param start The (deepest) path to start the search from
   * @param search The file or folder to search for
   */
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
    /**
     * All the input files that a pip depends on
     */
    dependencies: Transformer.InputArtifact[],

    /**
     * All the directories a pip can produce outputs to
     */
    outputs: Transformer.Output[],

    /**
     * All the file paths for BuildXL to ignore access to
     */
    untrackedPaths?: (File | Directory)[],

    /**
     * All the directories for BuildXL to ignore access to
     */
    untrackedScopes?: Directory[]
  }
  
  interface RushBuildArguments extends RushArguments {
    /**
     * The name of the package to build
     */
    target: string
  }
}
