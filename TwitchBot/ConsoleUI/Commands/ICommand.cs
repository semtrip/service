using System.Threading.Tasks;

namespace TwitchBot.ConsoleUI.Commands
{
    public interface ICommand
    {
        Task Execute();
    }
}