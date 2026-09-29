// Galaxy Royale's widget extension: the home-screen widget (your next timers,
// an incoming raid, might and resources) and the Live Activity for a raid on
// the colony. The game writes a small JSON snapshot to the App Group whenever
// it saves (Game/HomeWidget.cs); timers are absolute dates, so the widget
// keeps counting down correctly while the game is closed.
import SwiftUI
import WidgetKit
#if canImport(ActivityKit)
import ActivityKit
#endif

// MARK: - The snapshot

struct GalaxySnapshot: Codable {
    struct Timer: Codable, Hashable {
        var label: String
        var ends: Double
        var endDate: Date { Date(timeIntervalSince1970: ends) }
    }

    struct Raid: Codable, Hashable {
        var attacker: String
        var ends: Double
        var arrives: Date { Date(timeIntervalSince1970: ends) }
    }

    struct Boss: Codable, Hashable {
        var hull: Int
        var leaves: Double
    }

    var at: Double
    var name: String
    var level: Int
    var might: Int
    var gold: Int
    var quartz: Int
    var helium: Int
    var timers: [Timer]
    var raid: Raid?
    var core: Bool
    var boss: Boss?

    static let placeholder = GalaxySnapshot(
        at: Date().timeIntervalSince1970, name: "Commander", level: 7, might: 24_812,
        gold: 182_000, quartz: 96_400, helium: 41_900,
        timers: [
            Timer(label: "Gold Mine Lv 6", ends: Date().addingTimeInterval(19 * 60).timeIntervalSince1970),
            Timer(label: "Weapons Calibration Lv 3", ends: Date().addingTimeInterval(52 * 60).timeIntervalSince1970),
        ],
        raid: nil, core: false, boss: nil)

    static func load() -> GalaxySnapshot? {
        guard let group = Bundle.main.object(forInfoDictionaryKey: "GRAppGroup") as? String,
              let json = UserDefaults(suiteName: group)?.string(forKey: "snapshot"),
              let data = json.data(using: .utf8) else { return nil }
        return try? JSONDecoder().decode(GalaxySnapshot.self, from: data)
    }

    /// Timers still running at `date`, soonest first.
    func running(at date: Date) -> [Timer] {
        timers.filter { $0.endDate > date }.sorted { $0.ends < $1.ends }
    }
}

// MARK: - Timeline

struct GalaxyEntry: TimelineEntry {
    let date: Date
    let snapshot: GalaxySnapshot?
}

struct GalaxyProvider: TimelineProvider {
    func placeholder(in context: Context) -> GalaxyEntry {
        GalaxyEntry(date: Date(), snapshot: .placeholder)
    }

    func getSnapshot(in context: Context, completion: @escaping (GalaxyEntry) -> Void) {
        completion(GalaxyEntry(date: Date(), snapshot: context.isPreview ? .placeholder : GalaxySnapshot.load() ?? .placeholder))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<GalaxyEntry>) -> Void) {
        let snapshot = GalaxySnapshot.load()
        let now = Date()
        // A new entry as each timer (or the raid) lands, so finished ones drop off.
        var dates = [now]
        if let s = snapshot {
            dates += s.running(at: now).prefix(8).map(\.endDate)
            if let raid = s.raid, raid.arrives > now { dates.append(raid.arrives) }
        }
        let entries = Array(Set(dates)).sorted().map { GalaxyEntry(date: $0, snapshot: snapshot) }
        completion(Timeline(entries: entries, policy: .after(now.addingTimeInterval(3600))))
    }
}

// MARK: - Views

private let panel = Color(red: 0.043, green: 0.07, blue: 0.125)
private let accent = Color(red: 0.5, green: 0.83, blue: 1.0)
private let energy = Color(red: 1.0, green: 0.82, blue: 0.4)
private let danger = Color(red: 1.0, green: 0.48, blue: 0.48)
private let dim = Color(red: 0.53, green: 0.57, blue: 0.65)

private func short(_ n: Int) -> String {
    switch n {
    case 1_000_000...: return String(format: "%.1fM", Double(n) / 1_000_000)
    case 10_000...: return String(format: "%.1fK", Double(n) / 1_000)
    default: return "\(n)"
    }
}

struct GalaxyWidgetView: View {
    @Environment(\.widgetFamily) private var family
    let entry: GalaxyEntry

    var body: some View {
        Group {
            if let s = entry.snapshot {
                if family == .systemSmall { small(s) } else { medium(s) }
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    Text("GALAXY ROYALE").font(.caption2.bold()).foregroundColor(accent)
                    Text("Open the game to link this widget.").font(.caption).foregroundColor(dim)
                }
            }
        }
        .modifier(PanelBackground())
    }

    @ViewBuilder
    private func raidBanner(_ raid: GalaxySnapshot.Raid) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text("RAID INBOUND").font(.caption2.bold()).foregroundColor(danger)
            Text(raid.arrives, style: .timer).font(.title3.monospacedDigit().bold()).foregroundColor(danger)
            Text(raid.attacker).font(.caption2).foregroundColor(dim).lineLimit(1)
        }
    }

    @ViewBuilder
    private func timerRow(_ t: GalaxySnapshot.Timer) -> some View {
        HStack(spacing: 4) {
            Text(t.label).font(.caption2).foregroundColor(.white).lineLimit(1)
            Spacer(minLength: 2)
            Text(t.endDate, style: .timer).font(.caption2.monospacedDigit()).foregroundColor(accent)
                .multilineTextAlignment(.trailing).frame(maxWidth: 56, alignment: .trailing)
        }
    }

    private func small(_ s: GalaxySnapshot) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("GALAXY ROYALE").font(.caption2.bold()).foregroundColor(accent)
            if let raid = s.raid, raid.arrives > entry.date {
                raidBanner(raid)
            } else if let next = s.running(at: entry.date).first {
                Text(next.label).font(.caption).foregroundColor(.white).lineLimit(2)
                Text(next.endDate, style: .timer).font(.title3.monospacedDigit().bold()).foregroundColor(accent)
            } else {
                Text("Every queue is idle").font(.caption).foregroundColor(energy).lineLimit(2)
            }
            Spacer(minLength: 0)
            Text("Lv \(s.level) · might \(short(s.might))").font(.caption2).foregroundColor(dim)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func medium(_ s: GalaxySnapshot) -> some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text("GALAXY ROYALE").font(.caption2.bold()).foregroundColor(accent)
                Text("\(s.name) · Lv \(s.level)").font(.caption).foregroundColor(.white).lineLimit(1)
                Text("Might \(short(s.might))").font(.caption.bold()).foregroundColor(energy)
                Spacer(minLength: 0)
                Text("Gold \(short(s.gold))").font(.caption2).foregroundColor(dim)
                Text("Quartz \(short(s.quartz))").font(.caption2).foregroundColor(dim)
                Text("Helium \(short(s.helium))").font(.caption2).foregroundColor(dim)
            }
            .frame(maxWidth: 120, alignment: .leading)
            VStack(alignment: .leading, spacing: 4) {
                if let raid = s.raid, raid.arrives > entry.date {
                    raidBanner(raid)
                }
                let running = s.running(at: entry.date)
                if running.isEmpty {
                    Text("Every queue is idle — open the game to start the next upgrade.")
                        .font(.caption2).foregroundColor(energy)
                } else {
                    ForEach(running.prefix(s.raid == nil ? 4 : 2), id: \.self) { timerRow($0) }
                }
                Spacer(minLength: 0)
                if let boss = s.boss, Date(timeIntervalSince1970: boss.leaves) > entry.date {
                    Text("Dreadnought · \(boss.hull)% hull").font(.caption2).foregroundColor(danger)
                } else if s.core {
                    Text("You hold the Galactic Core").font(.caption2).foregroundColor(energy)
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }
}

/// iOS 17 wants the widget's background declared; older systems just paint it.
struct PanelBackground: ViewModifier {
    func body(content: Content) -> some View {
        if #available(iOSApplicationExtension 17.0, *) {
            content.containerBackground(panel, for: .widget)
        } else {
            content.padding().background(panel)
        }
    }
}

struct GalaxyWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "GalaxyWidget", provider: GalaxyProvider()) { entry in
            GalaxyWidgetView(entry: entry)
        }
        .configurationDisplayName("Galaxy Royale")
        .description("Your next timers, incoming raids and might.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

// MARK: - The raid Live Activity

#if canImport(ActivityKit)
@available(iOS 16.1, *)
struct RaidLiveActivity: Widget {
    var body: some WidgetConfiguration {
        ActivityConfiguration(for: RaidAttributes.self) { context in
            let s = context.state
            HStack(spacing: 12) {
                Image(systemName: "exclamationmark.triangle.fill").font(.title2).foregroundColor(danger)
                VStack(alignment: .leading, spacing: 2) {
                    Text("RAID INBOUND").font(.caption.bold()).foregroundColor(danger)
                    Text(s.ships > 0 ? "\(s.attacker) · \(s.ships) ships" : s.attacker)
                        .font(.subheadline).foregroundColor(.white).lineLimit(1)
                }
                Spacer()
                Text(timerInterval: Date()...max(s.arrives, Date()), countsDown: true)
                    .font(.title2.monospacedDigit().bold()).foregroundColor(danger)
                    .multilineTextAlignment(.trailing).frame(width: 90)
            }
            .padding()
            .activityBackgroundTint(panel)
            .activitySystemActionForegroundColor(accent)
        } dynamicIsland: { context in
            let s = context.state
            return DynamicIsland {
                DynamicIslandExpandedRegion(.leading) {
                    Label("Raid", systemImage: "exclamationmark.triangle.fill").foregroundColor(danger)
                }
                DynamicIslandExpandedRegion(.trailing) {
                    Text(timerInterval: Date()...max(s.arrives, Date()), countsDown: true)
                        .monospacedDigit().foregroundColor(danger).frame(width: 64)
                }
                DynamicIslandExpandedRegion(.bottom) {
                    Text(s.ships > 0 ? "\(s.attacker) · \(s.ships) ships inbound" : "\(s.attacker) inbound")
                        .font(.caption).foregroundColor(.white).lineLimit(1)
                }
            } compactLeading: {
                Image(systemName: "exclamationmark.triangle.fill").foregroundColor(danger)
            } compactTrailing: {
                Text(timerInterval: Date()...max(s.arrives, Date()), countsDown: true)
                    .monospacedDigit().foregroundColor(danger).frame(width: 44)
            } minimal: {
                Image(systemName: "exclamationmark.triangle.fill").foregroundColor(danger)
            }
        }
    }
}
#endif

// MARK: - The bundle

@main
struct GalaxyWidgets: WidgetBundle {
    var body: some Widget {
        GalaxyWidget()
        #if canImport(ActivityKit)
        RaidLiveActivity()
        #endif
    }
}
