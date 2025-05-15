import BalanceChart from '../Components/Charts/BalanceChart/BalanceChart'
import CategoryChart from '../Components/Charts/CategoryChart/CategoryChart'
import HistoryPanel from '../Components/HistoryPanel/HistoryPanel'
import PageContent from '../Components/PageContent/PageContent'
import type { Transaction } from '../Models/Transaction'
import transactionHistory from '../TestData/transactionHistory.json'

type Props = {}

const history: Transaction[] = transactionHistory
  .map((item) => ({
    ...item,
    date: new Date(item.date),
  }))
  .sort((a, b) => b.date.getTime() - a.date.getTime());

const HomePage = (props: Props) => {
  return (
    <PageContent>
        <BalanceChart history={history} />
        <CategoryChart history={history}/>
        <HistoryPanel history={history} />
    </PageContent>
  )
}

export default HomePage