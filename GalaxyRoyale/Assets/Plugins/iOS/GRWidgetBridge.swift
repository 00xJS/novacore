// App side of the home-screen widget and the raid Live Activity, called from
// C# (Game/HomeWidget.cs) through @_cdecl functions:
//   _GRWidgetWrite(json)                     — the snapshot the widget draws
//   _GRRaidActivityUpdate(attacker, arrives, ships) / _GRRaidActivityEnd()
// The App Group comes from Info.plist (GRAppGroup, written by the build). A
// build without the widget has no group, and every call does nothing.
import Foundation
import WidgetKit
#if canImport(ActivityKit)
import ActivityKit
#endif

private func appGroup() -> String? {
    Bundle.main.object(forInfoDictionaryKey: "GRAppGroup") as? String
}

@_cdecl("_GRWidgetWrite")
public func GRWidgetWrite(_ json: UnsafePointer<CChar>?) {
    guard let json = json, let group = appGroup(), let defaults = UserDefaults(suiteName: group) else { return }
    defaults.set(String(cString: json), forKey: "snapshot")
    WidgetCenter.shared.reloadAllTimelines()
}

@_cdecl("_GRRaidActivityUpdate")
public func GRRaidActivityUpdate(_ attacker: UnsafePointer<CChar>?, _ arrivesEpoch: Double, _ ships: Int32) {
    guard appGroup() != nil else { return }
    let name = attacker.map { String(cString: $0) } ?? "Unknown contact"
    #if canImport(ActivityKit)
    if #available(iOS 16.2, *) {
        RaidActivity.update(attacker: name, arrives: Date(timeIntervalSince1970: arrivesEpoch), ships: Int(ships))
    }
    #endif
}

@_cdecl("_GRRaidActivityEnd")
public func GRRaidActivityEnd() {
    #if canImport(ActivityKit)
    if #available(iOS 16.2, *) { RaidActivity.endAll() }
    #endif
}

#if canImport(ActivityKit)
@available(iOS 16.2, *)
enum RaidActivity {
    static func update(attacker: String, arrives: Date, ships: Int) {
        let state = RaidAttributes.ContentState(attacker: attacker, arrives: arrives, ships: ships)
        // Stale a minute after impact: the lock screen stops pretending it's still coming.
        let content = ActivityContent(state: state, staleDate: arrives.addingTimeInterval(60))
        if let current = Activity<RaidAttributes>.activities.first {
            Task { await current.update(content) }
            return
        }
        guard ActivityAuthorizationInfo().areActivitiesEnabled else { return }
        _ = try? Activity.request(attributes: RaidAttributes(), content: content, pushType: nil)
    }

    static func endAll() {
        for activity in Activity<RaidAttributes>.activities {
            Task { await activity.end(nil, dismissalPolicy: .immediate) }
        }
    }
}
#endif
