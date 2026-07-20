namespace ArtaleAI.Domain.Input
{
    /// <summary>
    /// 鍵盤獨佔擁有者。Idle 表示無人持有；其餘互斥。
    /// 導航不列為 Owner：垂直換層僅 Preempt Combat，不長期佔租約。
    /// </summary>
    public enum InputOwner
    {
        Idle = 0,
        Combat = 1,
        Party = 2,
        ChangeChannel = 3,
        FarmDismiss = 4,
    }
}
