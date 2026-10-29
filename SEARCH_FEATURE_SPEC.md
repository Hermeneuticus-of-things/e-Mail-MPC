# Specificație: căutarea mesajelor

## Scop

Să permită utilizatorului să găsească mesaje rapid, fie introducând termeni obișnuiți, fie descriind în limbaj natural ce caută. Interpretarea căutării trebuie să fie vizibilă și ușor de corectat.

Aceasta este o specificație de produs pentru e-Mail MPC. Nu descrie implementarea internă a eM Client și nu afirmă că funcțiile propuse mai jos există în acel produs.

## Dovezi publice despre eM Client

Pagina oficială eM Client descrie funcția „Recherche en langage naturel” astfel: „Décrivez ce que vous cherchez en une phrase normale, et eM Client la convertit pour vous en mots-clés appropriés.” Tradus: utilizatorul descrie ce caută într-o propoziție obișnuită, iar aplicația o transformă în cuvinte-cheie potrivite.

Aceeași pagină descrie asistentul din bara laterală ca putând ajuta, în limbaj obișnuit, la găsirea unui e-mail mai vechi.

- Sursă: [site-ul oficial eM Client](https://www.emclient.com/)
- Accesată la: 28 octombrie 2026
- Tipul dovezii: descriere publică a funcției de produs

Aceste afirmații nu dezvăluie algoritmul de căutare, câmpurile indexate, regulile de ranking sau comportamentul exact al interfeței. Verificarea interactivă de mai jos acoperă doar acceptarea unei interogări și prezența rezultatelor, nu comportamentul complet al produsului. Prin urmare, cerințele de mai jos sunt o propunere pentru e-Mail MPC, nu o descriere exhaustivă a implementării eM Client.

## Dovezi din instalarea locală

Instalarea indicată, `C:\Program Files\eM Client`, conține versiunea `11.0.1054.0` a `MailClient.dll` și a componentelor numite `MailClient.Indexer.*`. Inspecția REA a fost statică; nu a încărcat și nu a executat codul aplicației.

În metadata din `MailClient.Storage.Mail.dll` apar:

- tipul `MailClient.Storage.Mail.Query.MailSort`, cu membri numiți `From`, `To`, `Subject`, `HasAttachments`, `HasBeenRead`, `Flagged`, `Folder`, `OriginationDate` și `ReceivedDate`;
- tipul `MailClient.Storage.Mail.Data.DbMailRepository`, cu membri numiți `GetQueryContext`, `IsUnreadQuery`, `GetCount` și `FindByOids`, precum și câmpul `oidsToIndex`;
- în `MailClient.Indexer.Server.dll`, tipul `MailClient.Indexer.Server.Program`, inclusiv membri `TryGetDbLocation` și `CreateItemIndexer`.

Aceste nume confirmă că instalarea include componente distincte de indexare și modele de interogare/filtrare pentru mesaje. Ele nu dovedesc singure că fiecare câmp este indexat, cum este compusă o căutare, ce backend o execută sau cum sunt ordonate rezultatele. Aplicația a fost deschisă ulterior pentru un test UI restrâns; nu am deschis mesaje și nu am citit conținutul acestora.

**Metodă și limite:** `rea-agents inspect-managed-members` a citit metadata PE/CLI din DLL-uri. Rezultatele sunt indicii structurale, nu cod-sursă original și nici o urmărire completă a fluxului de execuție.

## Test manual al aplicației instalate

La cererea utilizatorului, aplicația instalată a fost pornită și a rămas deschisă și responsivă. Prin Windows UI Automation am identificat câmpul cu AutomationId `controlSearchTextBox`, etichetat „Search”.

Am introdus interogarea neutră `the`. După confirmare, interfața a expus un control de tip tabel cu `RowCount = 192`. Am numărat structura accesibilă, fără să citesc sau să afișez numele, subiectele ori conținutul mesajelor. Înainte de test am salvat valoarea existentă din câmp; după test am restaurat-o și am verificat că valoarea inițială a revenit.

Acest test confirmă că aplicația acceptă o interogare și expune rezultate într-un tabel accesibil. Nu validează relevanța celor 192 de rezultate, căutarea în fiecare câmp, comportamentul offline, viteza sau acuratețea căutării în limbaj natural. Captura REA nativă nu a fost disponibilă pentru acest țel Windows; testul interactiv a folosit UI Automation Windows.

## Experiența propusă

1. Utilizatorul introduce o expresie în câmpul global de căutare, de exemplu „mesajul cu factura trimis de Ana luna trecută”.
2. Aplicația transformă expresia într-un plan de căutare lizibil, de exemplu:
   - cuvinte: `factura`
   - expeditor: `Ana`
   - perioadă: luna trecută
3. Planul apare ca filtre editabile înainte sau odată cu afișarea rezultatelor.
4. Utilizatorul poate modifica filtrele sau reveni la căutarea literală dacă interpretarea nu este corectă.
5. Rezultatele indică folderul, expeditorul, subiectul, data și potrivirea relevantă.

## Cerințe funcționale pentru v1

- Căutare textuală în expeditor, destinatari, subiect și corpul mesajului.
- Filtrare explicită după folder, expeditor, destinatar, perioadă, necitit/citit, marcat și atașament.
- Căutarea obișnuită trebuie să funcționeze fără model AI și fără conexiune la internet, pentru datele deja indexate local.
- Interpretarea în limbaj natural trebuie să producă un plan structurat; utilizatorul poate inspecta și modifica filtrele.
- Dacă expresia nu poate fi interpretată cu suficientă încredere, aplicația o caută literal și nu ascunde mesaje pe baza unei presupuneri.
- Rezultatele pot fi sortate după relevanță sau dată, iar utilizatorul poate schimba sortarea.
- Interfața oferă ștergerea rapidă a interogării și păstrează filtrele cât timp utilizatorul revine la rezultate.
- Căutarea respectă permisiunile și conturile selectate; nu caută în date la care utilizatorul nu are acces.

## Confidențialitate și siguranță

- Indexul și căutarea de bază rămân locale.
- Nu se trimit mesaje, atașamente sau interogări către un serviciu extern fără acord explicit.
- Dacă se adaugă ulterior un model cloud, acesta trebuie să fie opțional, dezactivat implicit și să primească doar minimul necesar.
- Rezultatele nu trebuie să execute acțiuni asupra mesajelor. Mutarea, ștergerea sau arhivarea rămân acțiuni explicite.
- Jurnalele de diagnostic nu trebuie să păstreze textul mesajelor sau al interogărilor fără consimțământ.

## Criterii de acceptare

- Căutarea textuală găsește mesaje în câmpurile suportate și respectă filtrele alese.
- O interogare cu expeditor și perioadă produce filtre vizibile, modificabile și verificabile.
- O interpretare neclară nu elimină rezultatele posibile; se folosește căutarea literală.
- Căutarea locală funcționează în modul offline pentru conținutul deja sincronizat și indexat.
- Niciun conținut nu părăsește dispozitivul în fluxul de căutare implicit.
- Testele acoperă interogări goale, caractere Unicode, fusuri orare, intervale de date, filtre combinate și rezultate fără potrivire.

## Etapizare recomandată

### v0.2 — Căutare literală și filtre

- Index local pentru câmpurile de bază.
- Câmp global de căutare și filtre explicite.
- Teste pentru potrivire, permisiuni și actualizarea indexului.

### v0.3 — Interpretare în limbaj natural

- Conversie locală a expresiilor frecvente în filtre structurate.
- Previzualizare editabilă a interpretării.
- Revenire sigură la căutare literală când interpretarea este nesigură.

### După v0.3 — Relevanță și opțiuni extinse

- Ranking îmbunătățit, cu reguli documentate și rezultate explicabile.
- Căutare în conținutul atașamentelor doar după definirea limitelor de performanță și confidențialitate.
- Integrare opțională cu model AI numai după o decizie separată de produs și privacy.

## În afara scopului inițial

- Copierea codului, interfeței exacte sau algoritmilor interni ai eM Client.
- Trimiterea automată a conținutului poștei către un serviciu AI.
- Acțiuni automate asupra mesajelor pe baza unei interogări.
- Căutare în conținutul atașamentelor înainte de a exista un mecanism local, testat și controlabil.
