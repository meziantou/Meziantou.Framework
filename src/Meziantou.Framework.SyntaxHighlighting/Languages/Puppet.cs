using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages.Common;

namespace Meziantou.Framework.SyntaxHighlighting.Languages;

internal static class Puppet
{
    // The language keywords (typos included: highlight.js only uses these words in resource bodies).
    private const string KeywordList =
        "and case default else elsif false if in import enherits node or true undef unless main settings " +
        "$string";

    // The metaparameters, then the normal attributes.
    private const string LiteralList =
        "alias audit before loglevel noop require subscribe tag owner ensure group mode name|0 changes " +
        "context force incl lens load_path onlyif provider returns root show_diff type_check en_address " +
        "ip_address realname command environment hour monute month monthday special target weekday creates " +
        "cwd ogoutput refresh refreshonly tries try_sleep umask backup checksum content ctime force ignore " +
        "links mtime purge recurse recurselimit replace selinux_ignore_defaults selrange selrole seltype " +
        "seluser source souirce_permissions sourceselect validate_cmd validate_replacement allowdupe " +
        "attribute_membership auth_membership forcelocal gid ia_load_module members system host_aliases ip " +
        "allowed_trunk_vlans description device_url duplex encapsulation etherchannel native_vlan speed " +
        "principals allow_root auth_class auth_type authenticate_user k_of_n mechanisms rule session_owner " +
        "shared options device fstype enable hasrestart directory present absent link atboot blockdevice " +
        "device dump pass remounts poller_tag use message withpath adminfile allow_virtual allowcdrom " +
        "category configfiles flavor install_options instance package_settings platform responsefile status " +
        "uninstall_options vendor unless_system_user unless_uid binary control flags hasstatus manifest " +
        "pattern restart running start stop allowdupe auths expiry gid groups home iterations key_membership " +
        "keys managehome membership password password_max_age password_min_age profile_membership profiles " +
        "project purge_ssh_keys role_membership roles salt shell uid baseurl cost descr enabled enablegroups " +
        "exclude failovermethod gpgcheck gpgkey http_caching include includepkgs keepalive metadata_expire " +
        "metalink mirrorlist priority protect proxy proxy_password proxy_username repo_gpgcheck s3_enabled " +
        "skip_if_unavailable sslcacert sslclientcert sslclientkey sslverify mounted";

    // The core facts.
    private const string BuiltInList =
        "architecture augeasversion blockdevices boardmanufacturer boardproductname boardserialnumber cfkey " +
        "dhcp_servers domain ec2_ ec2_userdata facterversion filesystems ldom fqdn gid hardwareisa " +
        "hardwaremodel hostname id|0 interfaces ipaddress ipaddress_ ipaddress6 ipaddress6_ iphostnumber " +
        "is_virtual kernel kernelmajversion kernelrelease kernelversion kernelrelease kernelversion " +
        "lsbdistcodename lsbdistdescription lsbdistid lsbdistrelease lsbmajdistrelease lsbminordistrelease " +
        "lsbrelease macaddress macaddress_ macosx_buildversion macosx_productname macosx_productversion " +
        "macosx_productverson_major macosx_productversion_minor manufacturer memoryfree memorysize netmask " +
        "metmask_ network_ operatingsystem operatingsystemmajrelease operatingsystemrelease osfamily " +
        "partitions path physicalprocessorcount processor processorcount productname ps puppetversion " +
        "rubysitedir rubyversion selinux selinux_config_mode selinux_config_policy selinux_current_mode " +
        "selinux_current_mode selinux_enforced selinux_policyversion serialnumber sp_ sshdsakey sshecdsakey " +
        "sshrsakey swapencrypted swapfree swapsize timezone type uniqueid uptime uptime_days uptime_hours " +
        "uptime_seconds uuid virtual vlans xendomains zfs_version zonenae zones zpool_version";

    private const string IdentRe = @"([A-Za-z_]|::)(\w|::)*";

    public static CompiledMode Instance { get; } = Compiler.Compile(CreateMode());

    private static Mode CreateMode()
    {
        var comment = CommonModes.Comment("#", "$");
        var title = new Mode { Scope = "title", Begin = IdentRe };
        var variable = new Mode { Scope = "variable", Begin = @"\$" + IdentRe };
        var strings = new Mode
        {
            Scope = "string",
            Contains = [CommonModes.BackslashEscape, variable],
            Variants =
            [
                new Mode { Begin = "'", End = "'" },
                new Mode { Begin = "\"", End = "\"" },
            ],
        };

        return new Mode
        {
            Contains =
            [
                comment,
                variable,
                strings,
                // Deviation from highlight.js, whose class header only contains titles: the variables and the strings
                // of a parameter list (`class nginx (String $version = 'latest') {`) were split into titles.
                new Mode
                {
                    BeginKeywords = ["class"],
                    End = @"\{|;",
                    Illegal = "=",
                    Contains = [variable, strings, title, comment],
                },
                new Mode
                {
                    BeginKeywords = ["define"],
                    End = @"\{",
                    Contains = [new Mode { Scope = "section", Begin = CommonModes.IdentRe, EndsParent = true }],
                },

                // Only the first position of a word is tried: otherwise, each position of a long word that is not
                // followed by `{` would rescan the rest of it.
                new Mode
                {
                    Begin = CommonModes.RunStart(@"\w", "a-zA-Z") + CommonModes.IdentRe + @"\s+\{",
                    ReturnBegin = true,
                    End = @"\S",
                    Contains =
                    [
                        new Mode { Scope = "keyword", Begin = CommonModes.IdentRe },

                        // Deviation from highlight.js, where the resource continues after its body: the following
                        // words were highlighted as keywords (`include foo`), and the first other character was
                        // swallowed as the end of the resource (`$x` was not a variable, `# c` not a comment).
                        new Mode
                        {
                            Begin = @"\{",
                            End = @"\}",
                            EndsParent = true,
                            Keywords = Keywords.FromMap(new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["keyword"] = KeywordList,
                                ["literal"] = LiteralList,
                                ["built_in"] = BuiltInList,
                            }),
                            Contains =
                            [
                                strings,
                                comment,

                                // Only the first position of a word is tried (see above).
                                new Mode
                                {
                                    Begin = CommonModes.RunStart("a-zA-Z_") + @"[a-zA-Z_]+\s*=>",
                                    ReturnBegin = true,
                                    End = "=>",
                                    Contains = [new Mode { Scope = "attr", Begin = CommonModes.IdentRe }],
                                },
                                new Mode
                                {
                                    Scope = "number",
                                    Begin = @"(\b0[0-7_]+)|(\b0x[0-9a-fA-F_]+)|(\b[1-9][0-9_]*(\.[0-9_]+)?)|[0_]\b",
                                },
                                variable,
                            ],
                        },
                    ],
                },
            ],
        };
    }
}
