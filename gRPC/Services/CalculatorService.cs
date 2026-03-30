using Grpc.Core;
using GrpcServer;

namespace GrpcServer.Services
{
    public class CalculatorService : Calculator.CalculatorBase
    {
        public override Task<AddReply> Add(AddRequest request, ServerCallContext context)
        {
            Console.WriteLine($"Serwer otrzyma³: A={request.A}, B={request.B}");

            int wynik = request.A + request.B;

            Console.WriteLine($"Serwer odsy³a wynik: {wynik}");

            return Task.FromResult(new AddReply
            {
                Result = wynik
            });
        }
    }
}