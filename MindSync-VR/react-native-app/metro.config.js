const { getDefaultConfig, mergeConfig } = require('@react-native/metro-config');

const projectRoot = process.env.METRO_PROJECT_ROOT || __dirname;

/**
 * Metro configuration
 * https://reactnative.dev/docs/metro
 *
 * @type {import('@react-native/metro-config').MetroConfig}
 */
const config = {
  projectRoot,
  watchFolders: [...new Set([projectRoot, __dirname])],
  resolver: {
    // Native Android build trees are transient and can disappear while Metro's
    // fallback watcher is traversing them, which causes ENOENT crashes on Windows.
    blockList: /[/\\]android[/\\](?:\.cxx|build|app[/\\](?:\.cxx|build))[/\\]/,
  },
};

module.exports = mergeConfig(getDefaultConfig(projectRoot), config);
