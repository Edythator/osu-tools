# Edythator's version of osu!tools
this is a version of osu!tools which modifies PerformanceCalculator in order to make it support reading data from mysql servers. this is useful if you want to calculate the pp of entire profiles using the database dumps from [data.ppy.sh](https://data.ppy.sh). 


usage is simple; you edit line 152-155 in PerformanceCalculator/Profile/ProfileCommand.cs to input the details of your mysql server, then you compile, and you'll be done.

you'll end up with a PerformanceCalculator.dll that will have a new commandline argument, -db/--database, which can be set to either true or false, and it defaults to false.

oh yeah, this also fixes the DifficultyCommand.cs compiler errors so you don't have to manually patch them.

p.s: don't mind the sloppy code :(

# Licence

The osu! client code, framework, and tools are licensed under the [MIT licence](https://opensource.org/licenses/MIT). Please see [the licence file](LICENCE) for more information. [tl;dr](https://tldrlegal.com/license/mit-license) you can do whatever you want as long as you include the original copyright and license notice in any copy of the software/source.

Please note that this *does not cover* the usage of the "osu!" or "ppy" branding in any software, resources, advertising or promotion, as this is protected by trademark law.

Please also note that game resources are covered by a separate licence. Please see the [ppy/osu-resources](https://github.com/ppy/osu-resources) repository for clarifications.
