import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';
const errors=new Rate('registration_errors');
const vus=Number(__ENV.VUS||100);
const burst=__ENV.BURST==='1';
const once=__ENV.ONCE==='1';
export const options={
 scenarios:(burst||once)?{burst:{executor:'per-vu-iterations',vus,iterations:1,maxDuration:'120s'}}:{steady:{executor:'constant-vus',vus,duration:__ENV.DURATION||'60s'}},
 thresholds:{registration_errors:['rate<0.01'],http_req_failed:['rate<0.01'],http_req_duration:['p(95)<3000']},
 summaryTrendStats:['avg','med','p(90)','p(95)','p(99)','max'],
};
const base=__ENV.BASE_URL;
if(!base||__ENV.CONFIRM_SYNTHETIC_DATABASE!=='yes')throw Error('Set BASE_URL for a dedicated staging database and CONFIRM_SYNTHETIC_DATABASE=yes.');
export default function(){
 const get=http.get(base+'/');
 if(get.status!==200||!get.body){errors.add(true);return;}
 const doc=get.html();
 const csrf=doc.find('input[name="__RequestVerificationToken"]').attr('value');
 const token=doc.find('input[name="SubmissionToken"]').attr('value');
 if(!csrf||!token){errors.add(true);return;}
 if(burst)sleep(3);
 const response=http.post(base+'/',{
  __RequestVerificationToken:csrf,SubmissionToken:token,
  'Input.FirstName':'کاربر ساختگی '+__VU,'Input.LastName':'آزمون بار '+__ITER,
  'Input.Mobile':'','Input.Accounts[0].BankName':'سایر بانک‌ها',
  'Input.Accounts[0].OtherBankName':'بانک آزمایشی',
  'Input.Accounts[0].IBAN':'IR'+checksum('9990000000000000000000')+'9990000000000000000000',
 },{redirects:0});
 const ok=check(response,{'registered':r=>r.status===302&&r.headers.Location==='/Success'});
 errors.add(!ok);if(!burst)sleep(1);
}
function checksum(bban){let r=0;for(const c of bban+'182700')r=(r*10+Number(c))%97;return String(98-r).padStart(2,'0');}
export function handleSummary(data){const out={};out['test-results/k6-'+vus+(burst?'-burst':'')+'.json']=JSON.stringify(data,null,2);return out;}
