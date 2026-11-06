"""Collection credit follows historical discovery; preserve the .ever identities."""
import copy

DESCRIPTION = ('Completion follows Collected: whether each card variant has ever been obtained. '
               'Current binder ownership does not affect completion. Includes only real catalogue entries.')
OLD_DETAIL = 'Collected before uses the saved discovery flag. In binder uses the current ungraded binder quantity; cards on sale, in storage or being graded are separate.'
DETAIL = 'Collected uses the saved originally obtained indicator. Selling, displaying or grading the card does not remove collection credit.'


def obtained_only(definition):
    result=copy.deepcopy(definition)
    if result['Milestones'] not in (['Collected','In Binder'],['Collected']):
        raise ValueError('Unexpected collection fields')
    groups={}
    for item in result['Items']:
        group=item.get('GroupId')
        if not group or item['Id'] not in (group+'.ever',group+'.owned') or item['Target']!=1:
            raise ValueError('Unexpected collection identity')
        groups.setdefault(group,[]).append(item)
    if any(sum(i['Id'].endswith('.ever') for i in items)!=1 for items in groups.values()):
        raise ValueError('Each card needs exactly one discovery field')
    result['Items']=[i for i in result['Items'] if i['Id'].endswith('.ever')]
    for item in result['Items']:
        item['Milestone']='Collected'
        item['Detail']=item.get('Detail','').replace(OLD_DETAIL,DETAIL)
    result['Milestones']=['Collected']
    result['Description']=DESCRIPTION
    return result
