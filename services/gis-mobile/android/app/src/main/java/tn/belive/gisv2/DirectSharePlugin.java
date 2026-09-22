package tn.belive.gisv2;

import android.content.ActivityNotFoundException;
import android.content.Intent;

import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;

/**
 * Partage d'un texte DIRECTEMENT à une application précise (WhatsApp, Messenger) :
 * ACTION_SEND text/plain avec setPackage. C'est la voie standard d'Android, qui porte le
 * texte complet (plaque, adresse, date, lien) jusqu'à l'écran de composition.
 *
 * Pourquoi pas un lien : le schéma fb-messenger://share n'est pas documenté par Meta sur
 * Android et n'emporte qu'un lien (Messenger pouvait s'ouvrir sans rien de prérempli), et
 * la feuille de partage générique (plugin Share) oblige à chercher l'application dans la
 * liste. Lancer une activité d'une autre application n'est pas soumis à la visibilité des
 * paquets d'Android 11+ (seules les requêtes au PackageManager le sont) : pas de <queries>.
 * Application absente : ActivityNotFoundException, rendue comme { opened: false } pour que
 * l'écran se replie sur la feuille de partage.
 */
@CapacitorPlugin(name = "DirectShare")
public class DirectSharePlugin extends Plugin {

    @PluginMethod
    public void shareToPackage(PluginCall call) {
        String packageName = call.getString("packageName");
        String text = call.getString("text");
        if (packageName == null || packageName.isEmpty() || text == null) {
            call.reject("packageName et text sont requis");
            return;
        }

        Intent intent = new Intent(Intent.ACTION_SEND);
        intent.setType("text/plain");
        intent.putExtra(Intent.EXTRA_TEXT, text);
        intent.setPackage(packageName);

        JSObject result = new JSObject();
        try {
            getActivity().startActivity(intent);
            result.put("opened", true);
        } catch (ActivityNotFoundException e) {
            result.put("opened", false);
        }
        call.resolve(result);
    }
}
