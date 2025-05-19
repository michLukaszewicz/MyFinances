import { useEffect, useState } from 'react'
import BalanceChart from '../Components/Charts/BalanceChart/BalanceChart'
import CategoryChart from '../Components/Charts/CategoryChart/CategoryChart'
import HistoryPanel from '../Components/HistoryPanel/HistoryPanel'
import PageContent from '../Components/PageContent/PageContent'
import type { Transaction } from '../Models/Transaction'
import transactionService from '../Services/transactionService'

type Props = {}

const HomePage = (props: Props) => {
const [history, setHistory] = useState<Transaction[]>([]);

useEffect(() => {
  const fetchData = async () => {
    try {
      const response: Transaction[] = await transactionService.getTestData();
      setHistory(response);
      console.log('Fetched transactions:', response);
    } catch (error) {
      console.error('Error fetching transactions:', error);
    }
  }

  fetchData();
}, [])

  return (
    <PageContent>
        <BalanceChart history={history} />
        <CategoryChart history={history}/>
        <HistoryPanel history={history} />
    </PageContent>
  )
}

export default HomePage