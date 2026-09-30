#!/usr/bin/env python3
"""Make a redeem code (Settings › REDEEM A CODE): prints the entry to paste into
GalaxyRoyale/Assets/Scripts/Data/RedeemCodes.cs. Only the fingerprint goes in
the repo, never the code itself.

usage: scripts/redeem_code.py <CODE> [title] [gold quartz helium] [dark matter]
       scripts/redeem_code.py SPRING-2027 "Spring gift" 5000 5000 2000 100
"""
import hashlib
import sys

if len(sys.argv) < 2:
    sys.exit(__doc__)
code = "".join(c for c in sys.argv[1] if not c.isspace() and c != "-").upper()
title = sys.argv[2] if len(sys.argv) > 2 else "Gift"
gold, quartz, helium = (sys.argv[3:6] + ["0", "0", "0"])[:3]
dm = sys.argv[6] if len(sys.argv) > 6 else "0"
digest = hashlib.sha256(("galaxyroyale:" + code).encode()).hexdigest()
print(f"""            new RedeemCodeDef
            {{
                Hash = "{digest}",
                Title = "{title}",
                Resources = new ResourceBag({gold}, {quartz}, {helium}),
                DarkMatter = {dm},
            }},""")
