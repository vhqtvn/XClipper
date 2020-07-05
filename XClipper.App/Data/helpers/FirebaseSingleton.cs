﻿using Autofac;
using FireSharp.Core;
using FireSharp.Core.Config;
using FireSharp.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Components.DefaultSettings;
using static Components.TranslationHelper;
using static Components.FirebaseHelper;

#nullable enable

namespace Components
{
    public sealed class FirebaseSingleton
    {
        #region Variable Declaration

        private static FirebaseSingleton Instance;
        private IFirebaseClient client;
        private IFirebaseBinder binder;
        private string UID;
        private bool alwaysForceInvoke = false;
        private User user;
        private FirebaseData? currentAccount;

        /// <summary>
        /// Since <see cref="InitConfig(FirebaseData?)"/> is used in many cases it is not safe to call <br/>
        /// <see cref="SetCallback"/> more than once. This boolean will make sure to call it once.
        /// </summary>
        private bool isBinded = false;

        #endregion

        #region Singleton Constructor

        public static FirebaseSingleton GetInstance
        {
            get
            {
                if (Instance == null)
                    Instance = new FirebaseSingleton();
                return Instance;
            }
        }
        private FirebaseSingleton()
        { }

        #endregion

        #region Private Methods

        /// <summary>
        /// This will check if the access Token is valid or not.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> TaskCheckForAccessTokenValidity()
        {
            if (string.IsNullOrEmpty(FirebaseRefreshToken))
            {
                if (currentAccount != null)
                    binder.OnNeedToGenerateToken(currentAccount.Auth.ClientId, currentAccount.Auth.ClientSecret);
                else
                    MessageBox.Show(Translation.MSG_FIREBASE_USER_ERROR, Translation.MSG_ERR, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (currentAccount == null)
            {
                MessageBox.Show(Translation.MSG_FIREBASE_USER_ERROR, Translation.MSG_ERR, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (DateTime.Now.ToFormattedDateTime(false).ToInt() >= FirebaseTokenRefreshTime)
            {
                if (await RefreshAccessToken(currentAccount))
                {
                    // todo: Do something if token is refreshed.
                    return true;
                }else
                {
                    // todo: Do something if failed to refresh token.
                }
            }
            return false;
        }

       
        private async Task<User> _GetUser()
        {
            var data = await client.GetAsync($"users/{UID}");
            if (data.Body != "null")
            {
                return data.ResultAs<User>().Also((user) => { this.user = user; });
            }
            else return await RegisterUser();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Initializes the Firebase client. Must be called if credentials are changed.
        /// </summary>
        /// <param name="data"></param>
        public void InitConfig(FirebaseData? data = null)
        {
            UID = UniqueID;
            currentAccount = data;
            if (data != null)
            {
                FirebaseApiKey = data.ApiKey;
                FirebaseSecret = data.ApiSecret;
                FirebaseEndpoint = data.Endpoint;
                FirebaseAppId = data.AppId;
            }
            IFirebaseConfig config = new FirebaseConfig
            {
                AuthSecret = FirebaseSecret,
                BasePath = FirebaseEndpoint
            };
            client = new FirebaseClient(config);

            // BindUI is already set, make sure to set callback to it.
            SetCallback();
            Task.Run(async () => await SetGlobalUser(true));
        }

        /// <summary>
        /// This will submit configuration change to database.
        /// </summary>
        /// <returns></returns>
        public async Task SubmitConfigurations()
        {
            if (!BindDatabase) return;
            await SetGlobalUser();

            user.MaxItemStorage = DatabaseMaxItem;
            user.TotalConnection = DatabaseMaxConnection;
            user.IsLicensed = IsPurchaseDone;

            await client.SetAsync($"users/{UID}", user);
        }

        /// <summary>
        /// This will load the user from the firebase database.<br/>
        /// Returns True if user is valid.
        /// </summary>
        /// <param name="forceInvoke">Forcefully load the data even if user is not null.</param>
        /// <returns>True if user exist</returns>
        public async Task<bool> SetGlobalUser(bool forceInvoke = false)
        {
            
            if (client == null)
            {
                // todo: Do something when client isn't initialized
                return false;
                //MessageBox.Show()
            }
            if (!BindDatabase) return false;

            if (await TaskCheckForAccessTokenValidity() && (alwaysForceInvoke || user == null || forceInvoke))
            {
                user = await _GetUser();

                // todo: Set some other details for user...
                user.IsLicensed = IsPurchaseDone;
                user.TotalConnection = DatabaseMaxConnection;
                user.MaxItemStorage = DatabaseMaxItem;

                if (user.Devices != null && user.Devices.Count > 0)
                    alwaysForceInvoke = true;
            }
            return user != null;
        }

        /// <summary>
        /// Initialize the Instance with the UID supplied with it.
        /// </summary>
        /// <param name="UID"></param>
        public void Init(string UID) => this.UID = UID;

        /// <summary>
        /// This will be used to set binder at the start of the application.
        /// </summary>
        /// <param name="binder"></param>
        public void BindUI(IFirebaseBinder binder)
        {
            this.binder = binder;
        }

        /// <summary>
        /// This sets call back to the binder events with an attached interface.<br/>
        /// Must be used after <see cref="FirebaseSingleton.BindUI(IFirebaseBinder)"/>
        /// </summary>
        private async void SetCallback()
        {
            if (isBinded) return;
            await client.OnAsync($"users/{UID}", (o, a, c) =>
            {
                if (BindDatabase)
                    binder.OnDataAdded(a);
            },
            (o, a, c) =>
            {
                if (BindDatabase)
                    binder.OnDataChanged(a);
            },
            (o, a, c) =>
            {
                if (BindDatabase)
                    binder.OnDataRemoved(a);
            });
            isBinded = true;
        }

        /// <summary>
        /// Checks if the user exist in the nodes or not.
        /// </summary>
        /// <returns></returns>
        private async Task<bool> IsUserExist()
        {
            var response = await client.GetAsync($"users/{UID}");
            return response.Body != "null";
        }

        /// <summary>
        /// Add an empty user to the node.
        /// </summary>
        /// <returns></returns>
        public async Task<User> RegisterUser()
        {
            if (!BindDatabase) return new User();
            var exist = await IsUserExist();
            if (!exist)
            {
                var user = new User();
                user.IsLicensed = IsPurchaseDone;
                this.user = user;
                await client.SetAsync($"users/{UID}", user);
            }
            return user;
        }

        /// <summary>
        /// Removes all data associated with the UID.
        /// </summary>
        /// <returns></returns>
        public async Task RemoveUser()
        {
            await client.DeleteAsync($"users/{UID}");
            await RegisterUser();
        }

        /// <summary>
        /// Returns the user details.
        /// </summary>
        /// <returns></returns>
        public User GetUser()
        {
            return user;
        }

        public async Task<List<Device>?> GetDeviceListAsync()
        {
            if (!BindDatabase) return new List<Device>();

            if (await SetGlobalUser(true))
                return user.Devices;

            return new List<Device>();
        }

        public async Task<List<Device>> RemoveDevice(string DeviceId)
        {
            if (!BindDatabase) return new List<Device>();

            if (await SetGlobalUser(true))
            {
                user.Devices = user.Devices.Where(d => d.id != DeviceId).ToList();
                await client.UpdateAsync($"users/{UID}", user);
                return user.Devices;
            }

            return new List<Device>();
        }

        /// <summary>
        /// Add a clip data to the server instance.
        /// </summary>
        /// <param name="Text"></param>
        /// <returns></returns>
        public async Task AddClip(string? Text)
        {
            if (await SetGlobalUser())
            {
                if (Text == null) return;
                if (Text.Length > DatabaseMaxItemLength) return;
                if (user.Clips == null)
                    user.Clips = new List<Clip>();
                // Remove clip if greater than item
                if (user.Clips.Count > DatabaseMaxItem)
                    user.Clips.RemoveAt(0);
                user.Clips.Add(new Clip { data = Text.EncryptBase64(DatabaseEncryptPassword), time = DateTime.Now.ToFormattedDateTime(false) });
                await client.UpdateAsync($"users/{UID}", user);
            }
        }

        public async Task RemoveClip(int position)
        {
            if (await SetGlobalUser())
            {
                if (user.Clips == null)
                    return;
                user.Clips.RemoveAt(position);
                await client.UpdateAsync($"users/{UID}", user);
            }
        }

        /// <summary>
        /// Removes the clip data of user.
        /// </summary>
        /// <param name="Text"></param>
        /// <returns></returns>
        public async Task RemoveClip(string Text)
        {
            if (await SetGlobalUser())
            {
                if (Text == null) return;
                if (user.Clips == null)
                    return;
                foreach (var item in user.Clips)
                {
                    if (item.data.DecryptBase64(DatabaseEncryptPassword) == Text)
                    {
                        user.Clips.Remove(item);
                        await client.UpdateAsync($"users/{UID}", user);
                    }
                }
            }
        }

        /// <summary>
        /// Remove all clip data of user.
        /// </summary>
        /// <returns></returns>
        public async Task RemoveAllClip()
        {
            if (await SetGlobalUser())
            {
                if (user.Clips == null)
                    return;
                user.Clips.Clear();
                await client.UpdateAsync($"users/{UID}", user);
            }
        }

        #endregion

    }

    #region Entities

    public class User
    {

        /// <summary>
        /// Property tells whether the user has purchased license for this software or not.
        /// </summary>
        public bool IsLicensed { get; set; }

        /// <summary>
        /// Property tells the maximum number of device to be connected.
        /// </summary>
        public int TotalConnection { get; set; } = DatabaseMaxConnection;

        /// <summary>
        /// Property denotes the maximum this database can hold.
        /// </summary>
        public int MaxItemStorage { get; set; } = DatabaseMaxItem;

        /// <summary>
        /// Property tells the last connected Android device given its ID. Null means no one is connected.
        /// </summary>
        public List<Device>? Devices { get; set; }

        /// <summary>
        /// Property stores all the clip data.
        /// </summary>
        public List<Clip>? Clips { get; set; }
    }

    public class Device
    {
        public string id { get; set; }
        public int sdk { get; set; }
        public string model { get; set; }
    }

    public class Clip
    {
        public string data { get; set; }
        public string time { get; set; }
    }

    public class FirebaseData
    {
        public OAuth Auth { get; set; }
        public string Endpoint { get; set; }
        public string AppId { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
    }

    public class OAuth
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
    }

    #endregion
}
