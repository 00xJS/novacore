// The iOS share sheet for Report a problem (ProblemReport.cs): the player sees
// the report, then chooses Mail, Messages, Notes, copy or anything else. Nothing
// is sent unless they pick a destination.
#import <UIKit/UIKit.h>

extern UIViewController* UnityGetGLViewController();

extern "C" {

void _GRShareText(const char* text)
{
    if (text == NULL) return;
    NSString* body = [NSString stringWithUTF8String:text];
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* root = UnityGetGLViewController();
        if (root == nil) return;
        UIActivityViewController* share =
            [[UIActivityViewController alloc] initWithActivityItems:@[body] applicationActivities:nil];
        // iPad needs an anchor for the popover (harmless on iPhone).
        share.popoverPresentationController.sourceView = root.view;
        share.popoverPresentationController.sourceRect =
            CGRectMake(root.view.bounds.size.width / 2, root.view.bounds.size.height / 2, 1, 1);
        [root presentViewController:share animated:YES completion:nil];
    });
}

}
