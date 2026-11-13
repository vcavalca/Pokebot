# Translating Pokebot

All the texts of the Pokebot window live in [`src/Messages.resx`](src/Messages.resx) (English, the reference).
A translation is a copy of that file named `src/Messages.<code>.resx`, where `<code>` is a .NET language code:
a language (`fr`, `de`, `ja`) or a language and region (`pt-BR`, `es-MX`, `zh-Hans`).

Every translation file is embedded in `Pokebot.dll` and appears automatically in the **Language** list of the
**Settings** tab, under its own name (`Français`, `Português (Brasil)`...). No code or project change is needed.

## Add a new language

1. Fork the repository on GitHub and clone your fork.
2. Copy `src/Messages.resx` to `src/Messages.<code>.resx`, for example `src/Messages.pt-BR.resx`.
3. Translate the text inside each `<value>...</value>`. Do **not** change:
   - the `name="..."` of each entry: it is the key the code looks up;
   - placeholders such as `{0}` and `{1}`: they are replaced by numbers or names (you can move them inside the sentence);
   - Discord emoji codes such as `:x:` and `:white_check_mark:`;
   - XML escapes such as `&gt;` (`>`), `&lt;` (`<`) and `&amp;` (`&`).

   In-game words (menus, items, places such as `Bag > Key Items`, `Route 34`) can stay as players see them in their game.
4. Keep everything else in the file as it is and save it as UTF-8.
5. Add your language to the [Supported Languages](README.md#supported-languages) table of the README.
6. Commit and open a pull request. If you can't build the project, open the pull request anyway: it will be built and checked for you.

A translation doesn't have to be complete: a missing entry is shown in English.

## Improve an existing translation

Edit the `<value>` you want to fix in `src/Messages.<code>.resx` and open a pull request.

## Test your translation

1. Build the project with Visual Studio (`Pokebot.sln`) or with `dotnet build src/Pokebot.csproj -c Release`.
   The build copies the new `Pokebot.dll` to `BizHawk/ExternalTools/`.
2. Open `BizHawk/EmuHawk.exe`, then **Tools > External Tool > Pokebot**.
3. In the **Settings** tab, pick your language, then close and reopen Pokebot.
   Load a [supported ROM](README.md#supported-games) to see the texts of the other tabs.

## For developers: adding a new text

1. Add the entry to `src/Messages.resx` (English).
2. Add the matching property to `src/Messages.Designer.cs` (Visual Studio regenerates it when you save `Messages.resx`).
3. Add the translation to the `Messages.<code>.resx` files you can. The other languages show the English text until someone translates it.
