// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources.Models;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Storage.Models;
using Azure.ResourceManager.Storage;
using Microsoft.Identity.Client.Extensions.Msal;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;
using System.Xml.Linq;
using Microsoft.Extensions.Azure;
using System.Reflection.PortableExecutable;

namespace CreateSimpleInternetFacingLoadBalancer
{

    public class Program
    {
        private static ResourceIdentifier? _resourceGroupId = null;
        /**
         * Azure Network sample for creating a simple Internet facing load balancer -
         *
         * Summary ...
         *
         * - This sample creates a simple Internet facing load balancer that receives network traffic on
         *   port 80 and sends load-balanced traffic to two virtual machines
         *
         * Details ...
         *
         * 1. Create two virtual machines for the backend...
         * - in the same availability set
         * - in the same virtual network
         *
         * Create an Internet facing load balancer with ...
         * - A public IP address assigned to an implicitly created frontend
         * - One backend address pool with the two virtual machines to receive HTTP network traffic from the load balancer
         * - One load balancing rule for HTTP to map public ports on the load
         *   balancer to ports in the backend address pool

         * Delete the load balancer
         */

        public static async Task RunSample(ArmClient client)
        {
            string rgName = Utilities.CreateRandomName("NetworkSampleRG");
            string vnetName = Utilities.CreateRandomName("vnet");
            string loadBalancerName = Utilities.CreateRandomName("lb");
            string publicIpName = Utilities.CreateRandomName("pip");
            string availSetName = Utilities.CreateRandomName("av");
            string httpLoadBalancingRuleName = "httpRule";

            try
            {
                // Get default subscription
                SubscriptionResource subscription = await client.GetDefaultSubscriptionAsync();

                // Create a resource group in the EastUS region
                Utilities.Log($"Creating resource group...");
                ArmOperation<ResourceGroupResource> rgLro = await subscription.GetResourceGroups().CreateOrUpdateAsync(WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.WestUS));
                ResourceGroupResource resourceGroup = rgLro.Value;
                _resourceGroupId = resourceGroup.Id;
                Utilities.Log("Created a resource group with name: " + resourceGroup.Data.Name);

                //=============================================================
                // Define a common availability set for the backend virtual machines

                Utilities.Log("Creating an availability set ...");

                AvailabilitySetData availabilitySetInput = new AvailabilitySetData(resourceGroup.Data.Location)
                {
                    PlatformFaultDomainCount = 1,
                    PlatformUpdateDomainCount = 1,
                    Sku = new ComputeSku()
                    {
                        Name = "Aligned",
                    }
                };
                var availabilitySetLro = await resourceGroup.GetAvailabilitySets().CreateOrUpdateAsync(WaitUntil.Completed, availSetName, availabilitySetInput);
                AvailabilitySetResource availabilitySet = availabilitySetLro.Value;
                Utilities.Log($"Created first availability set: {availabilitySet.Data.Name}");

                //=============================================================
                // Define a common virtual network for the virtual machines

                Utilities.Log("Creating virtual network...");
                VirtualNetworkData vnetInput = new VirtualNetworkData()
                {
                    Location = resourceGroup.Data.Location,
                    AddressPrefixes = { "10.0.0.0/28" },
                    Subnets =
                    {
                        new SubnetData() { Name = "default", AddressPrefix = "10.0.0.8/29"},
                    },
                };
                var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
                VirtualNetworkResource vnet = vnetLro.Value;
                Utilities.Log($"Created a virtual network: {vnet.Data.Name}");

                //=============================================================
                // Create two virtual machines for the backend of the load balancer

                Utilities.Log("Creating two virtual machines in the frontend subnet ...\n"
                        + "and putting them in the shared availability set and virtual network.");

                List<ICreatable<IVirtualMachine>> virtualMachineDefinitions = new List<ICreatable<IVirtualMachine>>();

                for (int i = 0; i < 2; i++)
                {
                    virtualMachineDefinitions.Add(
                            azure.VirtualMachines.Define(Utilities.CreateRandomName("vm", 24))
                                .WithRegion(region)
                                .WithExistingResourceGroup(rgName)
                                .WithNewPrimaryNetwork(networkDefinition)
                                .WithPrimaryPrivateIPAddressDynamic()
                                .WithoutPrimaryPublicIPAddress()
                                .WithPopularLinuxImage(KnownLinuxVirtualMachineImage.UbuntuServer16_04_Lts)
                                .WithRootUsername(userName)
                                .WithSsh(sshKey)
                                .WithSize(VirtualMachineSizeTypes.Parse("Standard_D2a_v4"))
                                .WithNewAvailabilitySet(availabilitySetDefinition));
                }


                Stopwatch stopwatch = Stopwatch.StartNew();

                // Create and retrieve the VMs by the interface accepted by the load balancing rule
                var virtualMachines = azure.VirtualMachines.Create(virtualMachineDefinitions);

                stopwatch.Stop();
                Utilities.Log("Created 2 Linux VMs: (took " + (stopwatch.ElapsedMilliseconds / 1000) + " seconds)\n");

                // Print virtual machine details
                foreach(var vm in virtualMachines)
                {
                    Utilities.PrintVirtualMachine((IVirtualMachine)vm);
                }


                //=============================================================
                // Create an Internet facing load balancer
                // - implicitly creating a frontend with the public IP address definition provided for the load balancing rule
                // - implicitly creating a backend and assigning the created virtual machines to it
                // - creating a load balancing rule, mapping public ports on the load balancer to ports in the backend address pool

                Utilities.Log(
                        "Creating a Internet facing load balancer with ...\n"
                        + "- A frontend public IP address\n"
                        + "- One backend address pool with the two virtual machines\n"
                        + "- One load balancing rule for HTTP, mapping public ports on the load\n"
                        + "  balancer to ports in the backend address pool");

                var loadBalancer = azure.LoadBalancers.Define(loadBalancerName)
                        .WithRegion(region)
                        .WithExistingResourceGroup(rgName)

                        // Add a load balancing rule sending traffic from an implicitly created frontend with the public IP address
                        // to an implicitly created backend with the two virtual machines
                        .DefineLoadBalancingRule(httpLoadBalancingRuleName)
                            .WithProtocol(TransportProtocol.Tcp)
                            .FromNewPublicIPAddress(publicIpName)
                            .FromFrontendPort(80)
                            .ToExistingVirtualMachines(new List<IHasNetworkInterfaces>(virtualMachines))   // Convert VMs to the expected interface
                            .Attach()

                        .Create();

                // Print load balancer details
                Utilities.Log("Created a load balancer");
                Utilities.PrintLoadBalancer(loadBalancer);


                //=============================================================
                // Update a load balancer with 15 minute idle time for the load balancing rule

                Utilities.Log("Updating the load balancer ...");

                loadBalancer.Update()
                        .UpdateLoadBalancingRule(httpLoadBalancingRuleName)
                            .WithIdleTimeoutInMinutes(15)
                            .Parent()
                        .Apply();

                Utilities.Log("Updated the load balancer with a TCP idle timeout to 15 minutes");


                //=============================================================
                // Show the load balancer info

                Utilities.PrintLoadBalancer(loadBalancer);


                //=============================================================
                // Remove a load balancer

                Utilities.Log("Deleting load balancer " + loadBalancerName
                        + "(" + loadBalancer.Id + ")");
                azure.LoadBalancers.DeleteById(loadBalancer.Id);
                Utilities.Log("Deleted load balancer" + loadBalancerName);
            }
            finally
            {
                try
                {
                    if (_resourceGroupId is not null)
                    {
                        Utilities.Log($"Deleting Resource Group...");
                        await client.GetResourceGroupResource(_resourceGroupId).DeleteAsync(WaitUntil.Completed);
                        Utilities.Log($"Deleted Resource Group: {_resourceGroupId.Name}");
                    }
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception ex)
                {
                    Utilities.Log(ex);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                await RunSample(client);
            }
            catch (Exception ex)
            {
                Utilities.Log(ex);
            }
        }
    }
}