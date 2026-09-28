// Taptic Engine feedback for Galaxy Royale (GameAudio.Buzz → NativeHaptics).
// Generators are created once and re-prepared after each use so the next tap
// lands on the frame it's asked for. Unity's player loop runs on the main
// thread on iOS, which UIFeedbackGenerator requires.
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *GRImpact[5];
static UINotificationFeedbackGenerator *GRNotify;
static UISelectionFeedbackGenerator *GRSelect;

extern "C" {

// style: 0 light, 1 medium, 2 heavy, 3 soft, 4 rigid
void _GRHapticImpact(int style)
{
    if (style < 0 || style > 4) style = 0;
    if (GRImpact[style] == nil)
    {
        UIImpactFeedbackStyle s = UIImpactFeedbackStyleLight;
        switch (style)
        {
            case 1: s = UIImpactFeedbackStyleMedium; break;
            case 2: s = UIImpactFeedbackStyleHeavy; break;
            case 3: s = UIImpactFeedbackStyleSoft; break;
            case 4: s = UIImpactFeedbackStyleRigid; break;
            default: break;
        }
        GRImpact[style] = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
    }
    [GRImpact[style] impactOccurred];
    [GRImpact[style] prepare];
}

// type: 0 success, 1 warning, 2 error
void _GRHapticNotify(int type)
{
    if (GRNotify == nil) GRNotify = [[UINotificationFeedbackGenerator alloc] init];
    UINotificationFeedbackType t = type == 1 ? UINotificationFeedbackTypeWarning
                                 : type == 2 ? UINotificationFeedbackTypeError
                                 : UINotificationFeedbackTypeSuccess;
    [GRNotify notificationOccurred:t];
    [GRNotify prepare];
}

void _GRHapticSelection(void)
{
    if (GRSelect == nil) GRSelect = [[UISelectionFeedbackGenerator alloc] init];
    [GRSelect selectionChanged];
    [GRSelect prepare];
}

}
