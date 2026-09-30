// App Store in-app purchases (2026-09-30) through StoreKit 2, called from C#
// (Game/StoreService.cs) through @_cdecl functions. Everything the store says
// back is queued as a small JSON event the game polls once a frame:
//   _GRStoreStart()            listen for transactions (updates, unfinished ones)
//   _GRStoreLoad(idsCsv)       fetch products → {"type":"products","items":[…]}
//   _GRStoreBuy(productId)     buy → "purchased" | "pending" | "cancelled" | "failed"
//   _GRStorePoll()             next event, or NULL
//   _GRStoreFinish(txId)       the game has credited and saved it: finish it
// Only verified transactions reach the game, and each stays unfinished until
// the game says so, so a crash between paying and saving is never lost.
import Foundation
import StoreKit

private final class GRStoreState {
    static let shared = GRStoreState()
    private let lock = NSLock()
    private var events: [String] = []
    var products: [String: Any] = [:]           // id → Product (iOS 15+)
    var unfinished: [String: Any] = [:]         // transaction id → Transaction
    var started = false

    func push(_ event: [String: Any]) {
        guard let data = try? JSONSerialization.data(withJSONObject: event),
              let json = String(data: data, encoding: .utf8) else { return }
        lock.lock(); events.append(json); lock.unlock()
    }

    func pop() -> String? {
        lock.lock(); defer { lock.unlock() }
        return events.isEmpty ? nil : events.removeFirst()
    }
}

@available(iOS 15.0, *)
private func deliver(_ result: VerificationResult<Transaction>) async {
    switch result {
    case .verified(let tx):
        // Refunded or revoked: nothing to credit, just close it.
        if tx.revocationDate != nil { await tx.finish(); return }
        let id = String(tx.id)
        await MainActor.run { GRStoreState.shared.unfinished[id] = tx }
        GRStoreState.shared.push(["type": "purchased", "product": tx.productID, "transaction": id])
    case .unverified(_, let error):
        GRStoreState.shared.push(["type": "failed", "error": "The App Store couldn't verify the purchase (\(error.localizedDescription))"])
    }
}

@_cdecl("_GRStoreStart")
public func GRStoreStart() {
    guard #available(iOS 15.0, *) else { return }
    let state = GRStoreState.shared
    if state.started { return }
    state.started = true
    // Purchases made elsewhere, Ask to Buy approvals, renewals of nothing…
    Task.detached {
        for await result in Transaction.updates { await deliver(result) }
    }
    // Paid for but never finished (the app closed before the game saved).
    Task.detached {
        for await result in Transaction.unfinished { await deliver(result) }
    }
}

@_cdecl("_GRStoreLoad")
public func GRStoreLoad(_ idsCsv: UnsafePointer<CChar>?) {
    guard #available(iOS 15.0, *), let idsCsv = idsCsv else {
        GRStoreState.shared.push(["type": "products_failed", "error": "In-app purchases need iOS 15 or later"])
        return
    }
    let ids = String(cString: idsCsv).split(separator: ",").map(String.init)
    Task {
        do {
            let products = try await Product.products(for: ids)
            var items: [[String: Any]] = []
            await MainActor.run {
                for p in products { GRStoreState.shared.products[p.id] = p }
            }
            for p in products {
                items.append(["id": p.id, "price": p.displayPrice, "name": p.displayName])
            }
            GRStoreState.shared.push(["type": "products", "items": items])
        } catch {
            GRStoreState.shared.push(["type": "products_failed", "error": error.localizedDescription])
        }
    }
}

@_cdecl("_GRStoreBuy")
public func GRStoreBuy(_ productId: UnsafePointer<CChar>?) {
    guard #available(iOS 15.0, *), let productId = productId else { return }
    let id = String(cString: productId)
    Task {
        let product = await MainActor.run { GRStoreState.shared.products[id] as? Product }
        guard let product = product else {
            GRStoreState.shared.push(["type": "failed", "error": "That item isn't available from the App Store right now"])
            return
        }
        do {
            switch try await product.purchase() {
            case .success(let result): await deliver(result)
            case .pending: GRStoreState.shared.push(["type": "pending", "product": id])
            case .userCancelled: GRStoreState.shared.push(["type": "cancelled", "product": id])
            @unknown default: GRStoreState.shared.push(["type": "cancelled", "product": id])
            }
        } catch {
            GRStoreState.shared.push(["type": "failed", "error": error.localizedDescription])
        }
    }
}

@_cdecl("_GRStorePoll")
public func GRStorePoll() -> UnsafeMutablePointer<CChar>? {
    guard let json = GRStoreState.shared.pop() else { return nil }
    return strdup(json) // the runtime frees it after reading
}

@_cdecl("_GRStoreFinish")
public func GRStoreFinish(_ transactionId: UnsafePointer<CChar>?) {
    guard #available(iOS 15.0, *), let transactionId = transactionId else { return }
    let id = String(cString: transactionId)
    Task { @MainActor in
        guard let tx = GRStoreState.shared.unfinished.removeValue(forKey: id) as? Transaction else { return }
        await tx.finish()
    }
}
