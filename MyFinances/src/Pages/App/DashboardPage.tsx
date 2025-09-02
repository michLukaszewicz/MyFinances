import { useEffect, useState } from 'react'
import BalanceChart from '../../Components/Charts/BalanceChart/BalanceChart'
import CategoryChart from '../../Components/Charts/CategoryChart/CategoryChart'
import HistoryPanel from '../../Components/HistoryPanel/HistoryPanel'
import PageContent from '../../Components/PageContent/PageContent'
import type { Transaction } from '../../Models/Transaction'
import TransactionService from '../../Services/ApiServices/transactionService'

const HomePage = () => {
const [history, setHistory] = useState<Transaction[]>([]);

useEffect(() => {
  const fetchData = async () => {
    try {
      const response: Transaction[] = await TransactionService.getTransactionHistory();
      setHistory(response);
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