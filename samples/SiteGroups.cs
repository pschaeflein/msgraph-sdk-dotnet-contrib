using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Graph.Community.Samples
{
	public static class SiteGroups
	{
		public static async Task Run()
		{
      /////////////////////////////
      //
      // Programmer configuration
      //
      /////////////////////////////

      var sharepointDomain = "demo.sharepoint.com";
      var siteCollectionPath = "/sites/SiteGroupsTest";

			/////////////////
			//
			// Configuration
			//
			/////////////////

			AzureAdOptions azureAdOptions = new AzureAdOptions();

			var settingsFilename = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "appsettings.json");
			var builder = new ConfigurationBuilder()
													.AddJsonFile(settingsFilename, optional: false)
													.AddUserSecrets<Program>();
			var config = builder.Build();
			config.Bind("AzureAd", azureAdOptions);

			////////////////////////////
			//
			// Graph Client with Logger
			//
			////////////////////////////

			var logger = new StringBuilderHttpMessageLogger();
			/*
			 *  Could also use the Console if preferred...
			 *  
			 *  var logger = new ConsoleHttpMessageLogger();
			 */

			var pca = PublicClientApplicationBuilder
									.Create(azureAdOptions.ClientId)
									.WithTenantId(azureAdOptions.TenantId)
									.Build();

			var scopes = new string[] { $"https://{sharepointDomain}/AllSites.FullControl" };
			IAuthenticationProvider ap = new InteractiveAuthenticationProvider(pca, scopes); 

			using (LoggingMessageHandler loggingHandler = new LoggingMessageHandler(logger))
			using (HttpProvider hp = new HttpProvider(loggingHandler, false, new Serializer()))
			{
				GraphServiceClient graphServiceClient = new GraphServiceClient(ap, hp);

				////////////////////////////
				//
				// Setup is complete, run the sample
				//
				////////////////////////////

				var WebUrl = $"https://{sharepointDomain}{siteCollectionPath}";

				var web = await graphServiceClient
												.SharePointAPI(WebUrl)
												.Web
												.Request()
												.Expand(g => g.Users)
												.Expand("Owner")
												.GetAssociatedGroupsAsync();


				var group = web.AssociatedOwnerGroup;
				Console.WriteLine(group.Title);
				foreach (var user in group.Users)
				{
					Console.WriteLine($"  {user.LoginName}");
				}

				group = web.AssociatedMemberGroup;
				Console.WriteLine(group.Title);
				foreach (var user in group.Users)
				{
					Console.WriteLine($"  {user.LoginName}");
				}

				group = web.AssociatedVisitorGroup;
				Console.WriteLine(group.Title);
				foreach (var user in group.Users)
				{
					Console.WriteLine($"  {user.LoginName}");
				}


				Console.WriteLine("Press enter to show log");
				Console.ReadLine();
				Console.WriteLine();
				var log = logger.GetLog();
				Console.WriteLine(log);
			}
		}

	}
}