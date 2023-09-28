---
page_type: sample
languages:
- csharp
products:
- azure
extensions:
  services: virtual-network
  platforms: dotnet
---

# Getting started on creating simple Internet facing load balancers in C# #

 Azure Network sample for creating a simple Internet facing load balancer -
 Summary ...
 - This sample creates a simple Internet facing load balancer that receives network traffic on
   port 80 and sends load-balanced traffic to two virtual machines
 Details ...
 1. Create two virtual machines for the backend...
 - in the same availability set
 - in the same virtual network
 Create an Internet facing load balancer with ...
 - A public IP address assigned to an implicitly created frontend
 - One backend address pool with the two virtual machines to receive HTTP network traffic from the load balancer
 - One load balancing rule for HTTP to map public ports on the load
   balancer to ports in the backend address pool
 Delete the load balancer


## Running this Sample ##

To run this sample:

Set the environment variable `CLIENT_ID`,`CLIENT_SECRET`,`TENANT_ID`,`SUBSCRIPTION_ID` with the full path for an auth file. See [how to create an auth file](https://github.com/Azure/azure-libraries-for-net/blob/master/AUTH.md).

    git clone https://github.com/Azure-Samples/network-dotnet-create-simple-internet-facing-load-balancers.git

    cd network-dotnet-create-simple-internet-facing-load-balancers

    dotnet build

    bin\Debug\net452\CreateSimpleInternetFacingLoadBalancer.exe

## More information ##

[Azure Management Libraries for C#](https://github.com/Azure/azure-sdk-for-net)
[Azure .Net Developer Center](https://azure.microsoft.com/en-us/develop/net/)
If you don't have a Microsoft Azure subscription you can get a FREE trial account [here](http://go.microsoft.com/fwlink/?LinkId=330212)

---

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.