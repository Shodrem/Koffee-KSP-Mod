"""Static TP-25 storage/balance check against the local stock resource definitions.

No KSP code is executed. Resource-adjusted prices follow the agreed stock cost model.
"""
from pathlib import Path
from decimal import Decimal
import argparse, hashlib, json, re, sys
sys.dont_write_bytecode = True
SRC = Path(__file__).resolve().parent
OUT = SRC.parent
ROOT = SRC.parents[5]
sys.path.insert(0, str(ROOT/'Utils'))
from check_crew_chute import parse_cfg


def check_resources(ksp=Path('D:/Juegos/KSP')):
    cfg = parse_cfg(OUT/'TechnicalPilonTP25.cfg')
    values = cfg['values']
    assert values['name'] == 'SHOTechnicalPilonTP25'
    assert Decimal(values['mass']) == Decimal('0.12')
    assert Decimal(values['cost']) == 800 and Decimal(values['entryCost']) == 1800
    resources = [n['values'] for n in cfg['nodes'] if n['name'] == 'RESOURCE']
    assert len(resources) == 2
    assert resources == [dict(name='ElectricCharge', amount='200', maxAmount='200'),
                         dict(name='MonoPropellant', amount='20', maxAmount='20')]
    # Only the stock selector and existing node visibility controllers; no resource consumers/generators.
    modules = [n['values'] for n in cfg['nodes'] if n['name'] == 'MODULE']
    assert [n['name'] for n in modules] == ['ModulePartVariants']+['ModuleKoffeeDockingVisibility']*6
    assert all(n['controlDockingPorts'] == 'false' for n in modules[1:])
    assert len([n for n in cfg['nodes'] if n['name'] == 'NODE']) == 8
    assert values['attachRules'] == '1,0,1,1,0'
    definitions = ksp/'GameData/Squad/Resources/ResourcesGeneric.cfg'
    raw = definitions.read_bytes(); text = raw.decode('utf-8', errors='strict')
    stock = {}
    for name, expected in [('ElectricCharge', ('0', '0')), ('MonoPropellant', ('0.004', '1.2'))]:
        match = re.search(r'RESOURCE_DEFINITION\s*\{\s*name\s*=\s*'+name+r'\s*\n([^{}]*)', text)
        assert match, name
        stock[name] = {key: Decimal(re.search(r'^\s*'+key+r'\s*=\s*([\d.]+)', match[1], re.M)[1])
                       for key in ('density', 'unitCost')}
        assert (stock[name]['density'], stock[name]['unitCost']) == tuple(map(Decimal, expected))
    resource_mass = sum(Decimal(r['amount'])*stock[r['name']]['density'] for r in resources)
    fuel_price = sum(Decimal(r['amount'])*stock[r['name']]['unitCost'] for r in resources)
    dry = Decimal(values['mass']); full = dry+resource_mass
    assert resource_mass == Decimal('0.080') and full == Decimal('0.200') and fuel_price == 24
    empty_price = Decimal(values['cost'])-fuel_price
    assert empty_price == 776
    samples = []
    for fraction in (Decimal('0'), Decimal('0.5'), Decimal('1')):
        samples.append({'monopropellant_units': str(20*fraction),
                        'mass_kg': str((dry+resource_mass*fraction)*1000),
                        'price_funds': str(empty_price+fuel_price*fraction)})
    loc = parse_cfg(OUT/'Localization.cfg')
    sentences = {'es-es': ' Incluye una batería de 200 EC y una reserva de 20 unidades de monopropelente.',
                 'en-us': ' Includes a 200 EC battery and a reserve of 20 units of monopropellant.'}
    for language in loc['nodes']:
        assert language['values']['#LOC_Koffee_TP25_Description'].endswith(sentences[language['name']])
        assert language['values']['#LOC_Koffee_TP25_Title'] == 'SHO Technical Pilon TP-25'
    return {'result': 'PASS', 'scope': 'Static CFG and stock resource arithmetic only; KSP not executed',
            'dry_mass_kg': str(dry*1000), 'full_mass_kg': str(full*1000),
            'initial_resource_mass_kg': str(resource_mass*1000), 'initial_resource_value_funds': str(fuel_price),
            'initial_price_funds': values['cost'], 'entry_cost_funds': values['entryCost'],
            'capacities': {r['name']: r['maxAmount'] for r in resources},
            'monopropellant_fill_examples': samples, 'stock_resource_cfg': str(definitions),
            'stock_resource_cfg_sha256': hashlib.sha256(raw).hexdigest(),
            'ksp_editor_price_mass_test': False, 'ksp_external_battery_rcs_test': False,
            'ksp_save_load_test': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ksp', type=Path, default=Path('D:/Juegos/KSP'))
    parser.add_argument('--report', type=Path)
    options = parser.parse_args()
    report = check_resources(options.ksp)
    result = json.dumps(report, ensure_ascii=False, indent=2)+'\n'
    if options.report: options.report.write_bytes(result.encode('utf-8'))
    print(result)
