const { getDefaultConfig, mergeConfig } = require('@react-native/metro-config');

/**
 * Metro configuration
 * https://reactnative.dev/docs/metro
 *
 * @type {import('@react-native/metro-config').MetroConfig}
 */
const config = {
  resolver: {
    // Native Android build trees are transient and can disappear while Metro's
    // fallback watcher is traversing them, which causes ENOENT crashes on Windows.
    blockList: /[/\\]android[/\\](?:\.cxx|build|app[/\\](?:\.cxx|build))[/\\]/,
  },
};

module.exports = mergeConfig(getDefaultConfig(__dirname), config);
