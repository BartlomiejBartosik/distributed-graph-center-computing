using Grpc.Net.Client;
using System.Net.Http;
using GrpcServer;

try
{
    Console.WriteLine("Start klienta");

    var httpHandler = new HttpClientHandler();
    httpHandler.ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

    var channel = GrpcChannel.ForAddress("https://192.168.1.17:7185", new GrpcChannelOptions
    {
        HttpHandler = httpHandler
    });

    var client = new Calculator.CalculatorClient(channel);

    Console.WriteLine("Przed wysłaniem requestu");

    var response = await client.AddAsync(new AddRequest
    {
        A = 5,
        B = 7
    });

    Console.WriteLine("Po odpowiedzi z serwera");
    Console.WriteLine("Wynik: " + response.Result);
}
catch (Exception ex)
{
    Console.WriteLine("BLAD:");
    Console.WriteLine(ex.Message);
    Console.WriteLine(ex.ToString());
}

Console.WriteLine("Koniec programu");
Console.ReadKey();