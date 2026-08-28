package com.mindsyncvr.core.voice

/**
 * Where Component D (voice-based stress detection) lives. Kept separate from
 * [com.mindsyncvr.core.network.ApiConfig] because Cognify talks to TWO backends:
 * Component B (:8000, HRV) and Component D (:8010, voice). D handles its own B
 * integration internally, so the app only ever calls D.
 *
 * Dev hosts differ by target: the Android emulator reaches the dev machine via
 * 10.0.2.2; a physical phone needs the machine's LAN IP. Override [baseUrl] per
 * environment — never bake a dev host into a release build.
 */
data class ComponentDConfig(
    val baseUrl: String = DEFAULT_EMULATOR_BASE_URL,
    val useMock: Boolean = true,
    /** The first /infer lazy-loads the ~1.8 GB encoder (~1–2 min); reads must
     *  outlast that. Everything after is fast. */
    val readTimeoutSeconds: Long = 180,
    val connectTimeoutSeconds: Long = 30,
) {
    companion object {
        const val PORT = 8010

        /** Android emulator -> host-machine loopback. */
        const val DEFAULT_EMULATOR_BASE_URL = "http://10.0.2.2:$PORT/"

        /** Physical phone -> the dev machine's LAN IP, e.g. lan("192.168.1.20"). */
        fun lan(ip: String): String = "http://$ip:$PORT/"
    }
}
