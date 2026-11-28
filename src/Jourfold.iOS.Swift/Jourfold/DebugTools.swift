import Foundation

/// Tools that only help developers and testers: in builds with `JF_DEBUG_TOOLS=JOURFOLD_DEBUG_TOOLS`, never in others.
nonisolated enum DebugTools {
    #if JOURFOLD_DEBUG_TOOLS
    static let inBuild = true
    #else
    static let inBuild = false
    #endif

    /// Launch arguments (demo data, resetting, opening a page for a screenshot) are such a tool: other builds do not
    /// read them.
    static func flag(_ name: String) -> Bool { inBuild && UserDefaults.standard.bool(forKey: name) }
    static func value(_ name: String) -> String? { inBuild ? UserDefaults.standard.string(forKey: name) : nil }
}
