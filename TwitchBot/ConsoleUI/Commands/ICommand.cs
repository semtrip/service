using System.Threading.Tasks;

namespace TwitchViewerBot.ConsoleUI.Commands
{
    public interface ICommand
    {
        Task Execute();
    }
}