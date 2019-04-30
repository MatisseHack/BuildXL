import { Rush } from 'Sdk.NodeJs';
import { Transformer } from 'Sdk.Transformers';

const tempDir = d`./common/temp`;

// All pips will have these dependencies
const sharedDependencies = [
  f`./package.json`,
  f`./rush.json`,
  ...globR(d`./common/config/rush`),
];

// All pips will have these shared outputs
const sharedOutputs = [
  share(tempDir)
];

export const installPip = Rush.install({
  // All the files that the install step can read from
  dependencies: [
    ...sharedDependencies,
    ...globR(d`./common/scripts`),
    // Search the packages folder for and package.json file one level deeper
    ...glob(d`./packages`, "*/package.json")
  ],
  // All the directories where the pip can create outputs
  outputs: [
    ...sharedOutputs,
    share(d`./packages/simple-pkg/node_modules`)
  ]
});

export const buildPip = Rush.build({
  target: 'simple-pkg',
  // All the files that the install step can read from
  dependencies: [
    ...sharedDependencies,
    // Use some outputs of the install pip as input for this pip
    installPip.getOutputDirectory(tempDir),
    installPip.getOutputDirectory(d`./packages/simple-pkg/node_modules`),
    ...globR(d`./packages/simple-pkg`)
  ],
  // All the directories where the pip can create outputs
  outputs: [
    ...sharedOutputs,
    share(d`./packages/simple-pkg`)
  ]
});

/**
 * A convenience function to create a "shared" output directory.
 * Needed because Transformer.DirectoryOutput takes a lot of typing and objects don't map without an explicit cast.
 * 
 * @param directory The directory to be shared
 */
function share(directory: Directory): Transformer.DirectoryOutput {
  return { directory: directory, kind: 'shared' } as Transformer.DirectoryOutput;
}
