// Remove the credential fragment before Blazor initializes navigation/SignalR.
(()=>{const values=new URLSearchParams(location.hash.slice(1));if(values.has('code')&&values.has('server')){window.stikerPendingInvite={server:values.get('server'),code:values.get('code')};history.replaceState(null,'',location.pathname+location.search);}})();
