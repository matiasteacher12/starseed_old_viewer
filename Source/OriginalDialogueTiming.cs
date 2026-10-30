#if UNITY_EDITOR || VIEWER_BACKEND
// Reproduces TalkScriptEngine.TalkAction's one-character-per-update clock.
// The script supplies the fast interval; normal playback uses twice that value.
public sealed class OriginalDialogueTiming
{
    readonly int length;
    readonly float charDelay, endWait;
    float nextCharacter, remainingWait;
    public int VisibleCharacters { get; private set; }
    public bool IsTyping => VisibleCharacters < length;
    public bool Holding { get; private set; }

    public OriginalDialogueTiming(int length, float charDelay, float endWait)
    {
        this.length = length; this.charDelay = charDelay; this.endWait = endWait;
        remainingWait = endWait;
    }

    public void Tick(float deltaTime, int autoMode, bool voicePlaying)
    {
        if (Holding) return;
        if (IsTyping) {
            nextCharacter -= deltaTime;
            if (nextCharacter > 0) return;
            VisibleCharacters = charDelay <= 0 ? length : VisibleCharacters + 1;
            nextCharacter = charDelay * (autoMode == 2 ? 1 : 2);
            // Chapter1_3_01's Wait(0.3) follows the Talk action.
            remainingWait = endWait;
            return;
        }
        remainingWait -= deltaTime;
        // Original EndTalk is delayed by voice only when Auto is enabled.
        if (remainingWait <= 0 && (autoMode == 0 || !voicePlaying)) Holding = true;
    }

    public void RevealAll()
    {
        VisibleCharacters = length;
        remainingWait = endWait;
    }
}

#endif
