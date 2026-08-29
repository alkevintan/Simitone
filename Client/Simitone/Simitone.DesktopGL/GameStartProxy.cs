using FSO.Client;
using FSO.LotView;
using Simitone.Client;

namespace Simitone.DesktopGL
{
    public class GameStartProxy
    {
        public void Start()
        {
            GameFacade.DirectX = false;
            World.DirectX = false;
            SimitoneGame game = new SimitoneGame();

            game.Run();
            game.Dispose();
        }
    }
}
