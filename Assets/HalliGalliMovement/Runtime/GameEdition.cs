namespace HalliGalli.Movement
{
    public static class GameEdition
    {
        public static bool IsOfflineDemo {
            get {
#if BELL_RUN_OFFLINE_DEMO
                return true;
#else
                return false;
#endif
            }
        }
    }
}
