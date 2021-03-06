﻿using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Toolbelt.Blazor.I18nText.Interfaces;
using Toolbelt.Blazor.I18nText.Internals;

namespace Toolbelt.Blazor.I18nText
{
    public class I18nText : IDisposable
    {
        internal readonly I18nTextOptions Options = new I18nTextOptions();

        private string _CurrentLanguage = "en";

        private readonly WeakRefCollection<ComponentBase> Components = new WeakRefCollection<ComponentBase>();

        private Task InitLangTask;

        private readonly IServiceProvider ServiceProvider;

        private readonly IJSRuntime JSRuntime;

        private bool ScriptLoaded = false;

        private readonly I18nTextRepository I18nTextRepository;

        private readonly Guid ScopeId = Guid.NewGuid();

        internal I18nText(IServiceProvider serviceProvider)
        {
            this.I18nTextRepository = serviceProvider.GetService<I18nTextRepository>();

            this.ServiceProvider = serviceProvider;
            this.JSRuntime = serviceProvider.GetService<IJSRuntime>();
            this.Options.GetInitialLanguageAsync = GetInitialLanguageAsync;
            this.Options.PersistCurrentLanguageAsync = PersistCurrentLanguageAsync;
        }

        internal void InitializeCurrentLanguage()
        {
            this.InitLangTask = this.Options.GetInitialLanguageAsync.Invoke(this.ServiceProvider, this.Options)
                .AsTask()
                .ContinueWith(t => { _CurrentLanguage = t.IsFaulted ? CultureInfo.CurrentUICulture.Name : t.Result; });
        }

        private readonly SemaphoreSlim Syncer = new SemaphoreSlim(1, 1);

        private async ValueTask<IJSRuntime> GetJSRuntimeAsync()
        {
            if (!this.ScriptLoaded)
            {
                await Syncer.WaitAsync();
                try
                {
                    if (!this.ScriptLoaded)
                    {
                        const string scriptPath = "_content/Toolbelt.Blazor.I18nText/script.min.js";
                        await this.JSRuntime.InvokeVoidAsync("eval", "new Promise(r=>((d,t,s)=>(h=>h.querySelector(t+`[src=\"${{s}}\"]`)?r():(e=>(e.src=s,e.onload=r,h.appendChild(e)))(d.createElement(t)))(d.head))(document,'script','" + scriptPath + "'))");
                        this.ScriptLoaded = true;
                    }
                }
                catch (Exception) { }
                finally { Syncer.Release(); }
            }
            return this.JSRuntime;
        }

        private async ValueTask<string> GetInitialLanguageAsync(IServiceProvider serviceProvider, I18nTextOptions options)
        {
            var jsRuntime = await GetJSRuntimeAsync();
            return await jsRuntime.InvokeAsync<string>("Toolbelt.Blazor.I18nText.initLang", options.PersistanceLevel);
        }

        private async ValueTask PersistCurrentLanguageAsync(IServiceProvider serviceProvider, string langCode, I18nTextOptions options)
        {
            var jsRuntime = await GetJSRuntimeAsync();
            await jsRuntime.InvokeVoidAsync("Toolbelt.Blazor.I18nText.setCurrentLang", langCode, options.PersistanceLevel);
        }

        public async Task<string> GetCurrentLanguageAsync()
        {
            await EnsureInitialLangAsync();
            return _CurrentLanguage;
        }

        public async Task SetCurrentLanguageAsync(string langCode)
        {
            if (this._CurrentLanguage == langCode) return;

            if (this.Options.PersistCurrentLanguageAsync != null)
            {
                await this.Options.PersistCurrentLanguageAsync.Invoke(this.ServiceProvider, langCode, this.Options);
            }

            this._CurrentLanguage = langCode;
            await this.I18nTextRepository.ChangeLanguageAsync(this.ScopeId, this._CurrentLanguage);

            this.Components.InvokeStateHasChanged();
        }

        public async Task<T> GetTextTableAsync<T>(ComponentBase component) where T : class, I18nTextFallbackLanguage, new()
        {
            await EnsureInitialLangAsync();
            this.Components.Add(component);
            return await this.I18nTextRepository.GetTextTableAsync<T>(this.ScopeId, this._CurrentLanguage, singleLangInAScope: true);
        }

        private async Task EnsureInitialLangAsync()
        {
            var initLangTask = default(Task);
            lock (this) initLangTask = this.InitLangTask;
            if (initLangTask != null && !initLangTask.IsCompleted)
            {
                await initLangTask;
                lock (this) { this.InitLangTask?.Dispose(); this.InitLangTask = null; }
            }
        }

        public void Dispose()
        {
            this.I18nTextRepository.RemoveScope(this.ScopeId);
        }
    }
}
