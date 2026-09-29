// The Live Activity for an incoming raid — shared by the app (which starts,
// updates and ends it) and the widget extension (which draws it). Both targets
// compile this same file: Unity builds it into the app like any iOS plugin,
// and Editor/IosPostProcess.cs adds it to the extension too.
import Foundation

#if canImport(ActivityKit)
import ActivityKit

@available(iOS 16.1, *)
public struct RaidAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        /// Who's coming ("Unknown contact" until the radar can name them).
        public var attacker: String
        /// When the fleet lands on the colony.
        public var arrives: Date
        /// Ships in the fleet (0 = the radar can't count them yet).
        public var ships: Int

        public init(attacker: String, arrives: Date, ships: Int) {
            self.attacker = attacker
            self.arrives = arrives
            self.ships = ships
        }
    }

    public init() {}
}
#endif
