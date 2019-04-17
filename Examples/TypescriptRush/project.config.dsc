import { Rush } from 'Sdk.NodeJs';
import { Transformer } from 'Sdk.Transformers';

const tempDir = d`./common/temp`;

const sharedDependencies = [
  f`./package.json`,
  f`./rush.json`,
  ...globR(d`./common/config/rush`),
];

const sharedOutputs = [
  share(tempDir)
];

export const installPip = Rush.install({
  dependencies: [
    ...sharedDependencies,
    ...globR(d`./common/scripts`),
    ...glob(d`./packages`, "*/package.json")
  ],
  outputs: [
    ...sharedOutputs,
    share(d`./packages/simple-pkg/node_modules`)
  ]
});

export const buildPip = Rush.build({
  target: 'simple-pkg',
  dependencies: [
    ...sharedDependencies,
    installPip.getOutputDirectory(tempDir),
    installPip.getOutputDirectory(d`./packages/simple-pkg/node_modules`),
    ...globR(d`./packages/simple-pkg`)
  ],
  outputs: [
    ...sharedOutputs,
    share(d`./packages/simple-pkg`)
  ]
});

function share(directory: Directory): Transformer.DirectoryOutput {
  return { directory: directory, kind: 'shared' } as Transformer.DirectoryOutput;
}
