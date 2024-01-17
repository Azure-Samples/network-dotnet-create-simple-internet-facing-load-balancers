// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Compute;

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
            string vmName1 = Utilities.CreateRandomName("vm1-");
            string vmName2 = Utilities.CreateRandomName("vm2-");
            string httpLoadBalancingRuleName = "httpRule";
            string frontendName = loadBalancerName + "-FE";
            string backendPoolName = loadBalancerName + "-BAP";
            string httpProbe = "httpProbe";

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
                    AddressPrefixes = { "172.16.0.0/16" },
                    Subnets =
                    {
                        new SubnetData() { Name = "Front-end", AddressPrefix = "172.16.1.0/24"},
                    },
                };
                var vnetLro = await resourceGroup.GetVirtualNetworks().CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetInput);
                VirtualNetworkResource vnet = vnetLro.Value;
                Utilities.Log($"Created a virtual network: {vnet.Data.Name}");

                //=============================================================
                // Create two virtual machines for the backend of the load balancer

                Utilities.Log("Creating two virtual machines in the frontend subnet ...\n"
                        + "and putting them in the shared availability set and virtual network.");

                // Create vm1
                Utilities.Log("Creating a new virtual machine...");
                NetworkInterfaceResource nic1 = await Utilities.CreateNetworkInterface(resourceGroup, vnet);
                VirtualMachineData vmInput1 = Utilities.GetDefaultVMInputData(resourceGroup, vmName1);
                vmInput1.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic1.Id, Primary = true });
                var vmLro1 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName1, vmInput1);
                VirtualMachineResource vm1 = vmLro1.Value;
                Utilities.Log($"Created virtual machine: {vm1.Data.Name}");

                // Create vm2
                Utilities.Log("Creating a new virtual machine...");
                NetworkInterfaceResource nic2 = await Utilities.CreateNetworkInterface(resourceGroup, vnet);
                VirtualMachineData vmInput2 = Utilities.GetDefaultVMInputData(resourceGroup, vmName2);
                vmInput2.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference() { Id = nic2.Id, Primary = true });
                var vmLro2 = await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, vmName2, vmInput2);
                VirtualMachineResource vm2 = vmLro2.Value;
                Utilities.Log($"Created virtual machine: {vm2.Data.Name}");

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

                var frontendIPConfigurationId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName}/frontendIPConfigurations/{frontendName}");
                var backendAddressPoolId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName}/backendAddressPools/{backendPoolName}");
                LoadBalancerData loadBalancerInput = new LoadBalancerData()
                {
                    Location = resourceGroup.Data.Location,
                    Sku = new LoadBalancerSku()
                    {
                        Name = LoadBalancerSkuName.Standard,
                        Tier = LoadBalancerSkuTier.Regional,
                    },
                    // Explicitly define the frontend
                    FrontendIPConfigurations =
                    {
                        new FrontendIPConfigurationData()
                        {
                            Name = frontendName,
                            PrivateIPAllocationMethod = NetworkIPAllocationMethod.Dynamic,
                            Subnet = new SubnetData()
                            {
                                Id = vnet.Data.Subnets[0].Id
                            }
                        }
                    },
                    BackendAddressPools =
                    {
                        new BackendAddressPoolData()
                        {
                            Name = backendPoolName
                        }
                    },
                    // Add two rules that uses above backend and probe
                    LoadBalancingRules =
                    {
                        // Add a load balancing rule 
                        new LoadBalancingRuleData()
                        {
                            Name = httpLoadBalancingRuleName,
                            FrontendIPConfigurationId = frontendIPConfigurationId,
                            BackendAddressPoolId = backendAddressPoolId,
                            Protocol = LoadBalancingTransportProtocol.Tcp,
                            FrontendPort = 80,
                            BackendPort = 80,
                            EnableFloatingIP = false,
                            IdleTimeoutInMinutes = 15,
                            ProbeId = new ResourceIdentifier($"{resourceGroup.Id}/providers/Microsoft.Network/loadBalancers/{loadBalancerName}/probes/{httpProbe}"),
                        }
                    },
                    // Add two probes one per rule
                    Probes =
                    {
                        new ProbeData()
                        {
                            Name = httpProbe,
                            Protocol = ProbeProtocol.Http,
                            Port = 80,
                            IntervalInSeconds = 10,
                            NumberOfProbes = 2,
                            RequestPath = "/",
                        }
                    },
                };
                var loadBalancerLro = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName, loadBalancerInput);
                LoadBalancerResource loadBalancer = loadBalancerLro.Value;
                Utilities.Log($"Created a load balancer: {loadBalancer.Data.Name}");

                // Update nic to enable load balancer for vm
                Utilities.Log("Enable load balancer...");
                NetworkInterfaceData updateNicInput1 = nic1.Data;
                updateNicInput1.IPConfigurations.First().LoadBalancerBackendAddressPools.Add(new BackendAddressPoolData() { Id = backendAddressPoolId });
                _ = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, nic1.Data.Name, updateNicInput1);
                
                NetworkInterfaceData updateNicInput2 = nic2.Data;
                updateNicInput2.IPConfigurations.First().LoadBalancerBackendAddressPools.Add(new BackendAddressPoolData() { Id = backendAddressPoolId });
                _ = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, nic2.Data.Name, updateNicInput2);

                //=============================================================
                // Update a load balancer with 15 minute idle time for the load balancing rule

                Utilities.Log("Updating the load balancer ...");

                LoadBalancerData updateLoadBalancerInput = loadBalancer.Data;
                updateLoadBalancerInput.LoadBalancingRules.First(item => item.Name == httpLoadBalancingRuleName).IdleTimeoutInMinutes = 15;
                loadBalancerLro = await resourceGroup.GetLoadBalancers().CreateOrUpdateAsync(WaitUntil.Completed, loadBalancerName, loadBalancerInput);
                loadBalancer = loadBalancerLro.Value;

                Utilities.Log("Updated the load balancer with a TCP idle timeout to 15 minutes");

                //=============================================================
                // Show the load balancer info

                Utilities.Log("Name: " + loadBalancer.Data.Name);
                Utilities.Log("LoadBalancingRules: " + loadBalancer.Data.LoadBalancingRules.Count);
                Utilities.Log("Probes: " + loadBalancer.Data.Probes.Count);
                Utilities.Log("FrontendIPConfigurations: " + loadBalancer.Data.FrontendIPConfigurations.Count);
                Utilities.Log("BackendAddressPools: " + loadBalancer.Data.BackendAddressPools.Count);

                //=============================================================
                // Remove a load balancer

                Utilities.Log("Deleting load balancer... ");
                await loadBalancer.DeleteAsync(WaitUntil.Completed);
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