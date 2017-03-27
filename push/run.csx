#r "Microsoft.Azure.NotificationHubs"
#r "Newtonsoft.Json"
#r "OfficeDevPnP.Core.dll"
#r "Microsoft.SharePoint.Client.dll"
#r "Microsoft.WindowsAzure.Configuration.dll"
#r "Microsoft.SharePoint.Client.Runtime.dll"

using System;
using System.Net;
using Microsoft.Azure.NotificationHubs;
using Newtonsoft.Json;
using Microsoft.Azure;
using Microsoft.SharePoint.Client;
using OfficeDevPnP.Core;

public static async Task<object> Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info($"Webhook was triggered!");

    string validationToken = req.GetQueryNameValuePairs()
        .FirstOrDefault(q => string.Compare(q.Key, "validationtoken", true) == 0)
        .Value;
    
    // SUBSCRIBE TO WEBHOOKS
    if (validationToken != null)
    {
      log.Info($"Validation token {validationToken} received");
      var response = req.CreateResponse(HttpStatusCode.OK);
      response.Content = new StringContent(validationToken);
      return response;
    }

    // PUSH NOTIFICATION
    log.Info($"SharePoint triggered our webhook...great :-)");
    var content = await req.Content.ReadAsStringAsync();
    log.Info($"Received following payload: {content}");

    var notifications = JsonConvert.DeserializeObject<ResponseModel<NotificationModel>>(content).Value;
    log.Info($"Found {notifications.Count} notifications");

    if (notifications.Count > 0)
    {
        log.Info($"Processing notifications...");

        // CONNECT TO NOTIFICATION HUB
        NotificationHubClient Hub = NotificationHubClient.CreateClientFromConnectionString(Settings.NotificatioHubConnStr, Settings.NotificatioHubName);
     
        var am = new OfficeDevPnP.Core.AuthenticationManager();

        foreach(var notification in notifications)
        {
            string url = string.Format("https://{0}{1}", Settings.sharePointTenant, notification.SiteUrl);

            log.Info($"Accessing site...");

            // CONNECT TO SHAREPOINT
            using (var ctx = am.GetAzureADAppOnlyAuthenticatedContext(url, Settings.aadAppId, Settings.tenant, Settings.certificatePath, Settings.pfxPassword))
            {
                var nWeb = ctx.Site.OpenWebById(new Guid(notification.WebId));
                ctx.Load(nWeb); 
                ctx.ExecuteQueryRetry();

                log.Info($"Accessing cool...");

                Guid listId = new Guid(notification.Resource);
                
                List targetList = nWeb.Lists.GetById(listId);
                ctx.ExecuteQueryRetry();  
                
                ChangeToken lastChangeToken = null;

                ctx.Load(targetList.RootFolder, p => p.Properties);
                ctx.ExecuteQueryRetry();
                
                if ( !targetList.RootFolder.Properties.FieldValues.ContainsKey("lastChangeToken") )
                {
                    lastChangeToken = new ChangeToken();
                    lastChangeToken.StringValue = string.Format("1;3;{0};{1};-1", notification.Resource, DateTime.Now.AddMinutes(-10).ToUniversalTime().Ticks.ToString());
                }
                else
                {
                    lastChangeToken = new ChangeToken();
                    lastChangeToken.StringValue = targetList.RootFolder.Properties["lastChangeToken"].ToString();
                }

                ChangeQuery changeQuery = new ChangeQuery(false, false);
                changeQuery.Item = true;
                changeQuery.File = true;
                changeQuery.Add = true;
                changeQuery.Update = true;
                changeQuery.FetchLimit = 1000; // Max value is 2000, default = 1000

                changeQuery.ChangeTokenStart = lastChangeToken;

                // Execute the change query
                var changes = targetList.GetChanges(changeQuery);
                ctx.Load(changes);
                ctx.ExecuteQueryRetry();

                bool allChangesRead = false;
                do
                {
                    if (changes.Count > 0)
                    {
                        foreach (Change change in changes)
                        {
                            lastChangeToken = change.ChangeToken;

                            if (change is ChangeItem)
                            {
                                ChangeItem ci = change as ChangeItem;

                                ListItem li = targetList.GetItemById(ci.ItemId);  
                                ctx.Load(li);  
                                ctx.ExecuteQueryRetry(); 
                                var title = li["Title"];

                                // SEND PUSH NOTIFICATION
                                var alert = "{\"aps\":{\"alert\":\"" + title + "\"}}";
                                await Hub.SendAppleNativeNotificationAsync(alert);
                                log.Info($"Message pushed :-)"); 
                            }
                        }

                        // We potentially can have a lot of changes so be prepared to repeat the 
                        // change query in batches of 'FetchLimit' untill we've received all changes
                        if (changes.Count < changeQuery.FetchLimit)
                        {
                            allChangesRead = true;
                        }
                    }
                    else
                    {
                        allChangesRead = true;
                    }
                    // Are we done?
                } while (allChangesRead == false);

                // update the new chnagetoken to list rootfolder property
                targetList.RootFolder.Properties["lastChangeToken"] = lastChangeToken.StringValue;
                targetList.RootFolder.Update();
                ctx.ExecuteQuery();
            }
        }
    }

    // if we get here we assume the request was well received
    return new HttpResponseMessage(HttpStatusCode.OK);
}

public class Settings
{
    public static readonly string aadAppId;
    public static readonly string pfxPassword;
    public static readonly string tenant;
    public static readonly string certificatePath;
    public static readonly string sharePointTenant;
    public static readonly string NotificatioHubConnStr;
    public static readonly string NotificatioHubName;

    static Settings()
    {
        aadAppId = CloudConfigurationManager.GetSetting("AzureAD_aadAppId");
        pfxPassword = CloudConfigurationManager.GetSetting("AzureAD_pfxPassword");
        tenant = CloudConfigurationManager.GetSetting("AzureAD_tenant");
        certificatePath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), "site\\wwwroot\\Push\\", CloudConfigurationManager.GetSetting("AzureAD_certificatePath"));
        sharePointTenant = CloudConfigurationManager.GetSetting("SharePointTenant");
        NotificatioHubConnStr = CloudConfigurationManager.GetSetting("NotificatioHubConnStr");
        NotificatioHubName = CloudConfigurationManager.GetSetting("NotificatioHubName");

    }
}

// supporting classes
public class ResponseModel<T>
{
    [JsonProperty(PropertyName = "value")]
    public List<T> Value { get; set; }
}

public class NotificationModel
{
    [JsonProperty(PropertyName = "subscriptionId")]
    public string SubscriptionId { get; set; }

    [JsonProperty(PropertyName = "clientState")]
    public string ClientState { get; set; }

    [JsonProperty(PropertyName = "expirationDateTime")]
    public DateTime ExpirationDateTime { get; set; }

    [JsonProperty(PropertyName = "resource")]
    public string Resource { get; set; }

    [JsonProperty(PropertyName = "tenantId")]
    public string TenantId { get; set; }

    [JsonProperty(PropertyName = "siteUrl")]
    public string SiteUrl { get; set; }

    [JsonProperty(PropertyName = "webId")]
    public string WebId { get; set; }
}

public class SubscriptionModel
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; }

    [JsonProperty(PropertyName = "clientState", NullValueHandling = NullValueHandling.Ignore)]
    public string ClientState { get; set; }

    [JsonProperty(PropertyName = "expirationDateTime")]
    public DateTime ExpirationDateTime { get; set; }

    [JsonProperty(PropertyName = "notificationUrl")]
    public string NotificationUrl {get;set;}

    [JsonProperty(PropertyName = "resource", NullValueHandling = NullValueHandling.Ignore)]
    public string Resource { get; set; }
}
