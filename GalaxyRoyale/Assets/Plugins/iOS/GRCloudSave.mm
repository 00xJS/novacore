// iCloud save backup for Galaxy Royale (CloudSave.cs). The save rides in the
// app's iCloud key-value store — no container to create, it follows the
// player's Apple ID to a reinstall or a new phone, and a gzipped save (~40 KB)
// sits far inside the store's 1 MB quota. Needs the
// com.apple.developer.ubiquity-kvstore-identifier entitlement, which
// Editor/IosPostProcess.cs adds to the generated Xcode project.
#import <Foundation/Foundation.h>

static NSString *GRString(const char *utf8)
{
    return utf8 != NULL ? [NSString stringWithUTF8String:utf8] : nil;
}

extern "C" {

// 1 when the device is signed in to iCloud (writes still queue otherwise).
// (int, not bool: C# marshals bool returns as 4-byte BOOLs.)
int _GRCloudSignedIn(void)
{
    return [[NSFileManager defaultManager] ubiquityIdentityToken] != nil ? 1 : 0;
}

// Pull the latest values down (call at launch; they arrive asynchronously).
void _GRCloudSync(void)
{
    [[NSUbiquitousKeyValueStore defaultStore] synchronize];
}

// Store both halves of a backup together, then ask the store to sync.
// Returns 1 when the store accepted them.
int _GRCloudPut(const char *headerKey, const char *header, const char *dataKey, const char *data)
{
    NSString *hk = GRString(headerKey), *hv = GRString(header);
    NSString *dk = GRString(dataKey), *dv = GRString(data);
    if (hk == nil || hv == nil || dk == nil || dv == nil) return 0;
    NSUbiquitousKeyValueStore *store = [NSUbiquitousKeyValueStore defaultStore];
    [store setString:dv forKey:dk];
    [store setString:hv forKey:hk];
    return [store synchronize] ? 1 : 0;
}

// The string stored under key, or NULL. Returned malloc'd: the IL2CPP
// marshaller copies it into a managed string and frees it.
char *_GRCloudGet(const char *key)
{
    NSString *k = GRString(key);
    if (k == nil) return NULL;
    NSString *value = [[NSUbiquitousKeyValueStore defaultStore] stringForKey:k];
    if (value == nil) return NULL;
    const char *utf8 = [value UTF8String];
    size_t length = strlen(utf8) + 1;
    char *copy = (char *)malloc(length);
    memcpy(copy, utf8, length);
    return copy;
}

}
