package tn.belive.gisv2;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        // Plugins propres à l'application : à enregistrer AVANT super.onCreate, qui
        // construit le pont Capacitor.
        registerPlugin(DirectSharePlugin.class);
        super.onCreate(savedInstanceState);
    }
}
