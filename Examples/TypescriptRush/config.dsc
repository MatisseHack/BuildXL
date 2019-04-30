config({
  // Pulls in the main module.config.dsc and the modules in the sdk folder
  modules: [
    f`./module.config.dsc`,
    f`./sdk/Sdk.Transformers/package.config.dsc`,
    ...globR(d`./sdk`, "module.config.dsc")
  ],
  // Configuration that allows access to the folders needed by the build
  // You will get errors about "unable to hash files" if trackSourceFileChanges is false
  mounts: [
    // rush calls git, which can read any modified file in the repo so we need `allowUndeclaredSourceReads: true` for now
    {
      name: a`repo`,
      path: p`../..`,
      trackSourceFileChanges: true
    },
    {
      name: a`packages`,
      path: p`./packages`,
      isReadable: true,
      isWritable: true,
      trackSourceFileChanges: true
    },
    {
      name: a`common`,
      path: p`./common`,
      isReadable: true,
      isWritable: true,
      trackSourceFileChanges: true
    }
  ],
  // Used to ignore double writes to rush's lockfiles. Currently not working, but not sure why.
  cacheableFileAccessWhitelist: [
    {
      name: "TempLockfiles",
      toolPath: f`./Out/frontend/Download/NodeJs.osx-x64/c/node-v10.15.3-darwin-x64/bin/node`,
      pathRegex: ".*/rush#\\d+\\.lock"
    }
  ],
  resolvers: [
    {
      // Dependencies for the build script. In this case we need to download Node.js in order to run rush.
      // Not sure how to generate the VSO0 hashes
      kind: "Download",
      downloads: [
        {
          moduleName: "NodeJs.win-x64",
          url: "https://nodejs.org/download/release/v10.15.3/node-v10.15.3-win-x64.zip",
          // hash: "VSO0:95276E5CC1A0F5095181114C16734E8E0416B222F232E257E31FEBF73324BC2300",
          archiveType: "zip",
        },
        {
          moduleName: "NodeJs.osx-x64",
          url: "https://nodejs.org/download/release/v10.15.3/node-v10.15.3-darwin-x64.tar.gz",
          // hash: "VSO0:2D9315899B651CA8489F47580378C5C8EAE5E0DEB4F50AF5A149BEC7B387228000",
          archiveType: "tgz",
        },
        {
          moduleName: "NodeJs.linux-x64",
          url: "https://nodejs.org/download/release/v10.15.3/node-v10.15.3-linux-arm64.tar.gz",
          // hash: "VSO0:9DE138F52CCCE4B89747BFDEC5D3A0DDBB23BF80BB2A45AE0218D852845AB13C00",
          archiveType: "tgz",
        },
        {
          moduleName: "npm.osx",
          url: "https://drive.google.com/uc?id=1QYpA6HgAnOrSwCcoJ7Z4tvYSaxV-I2Zr&export=download",
          archiveType: "tgz",
        },
        {
          moduleName: "rush",
          url: "https://drive.google.com/uc?id=1ioJSnoLrsKmdY7fAY_mhr0pzK8Zf5qel&export=download",
          archiveType: "tgz",
        },
        {
          moduleName: "git",
          url: "https://drive.google.com/uc?id=1hx9GqrHLBspCxm0MgyR30sWzGznqk-Qt&export=download",
          archiveType: "tgz",
        },
      ],
    }
  ]
});
